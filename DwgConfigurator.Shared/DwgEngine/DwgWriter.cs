using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Tables;

namespace DwgConfigurator.Shared.DwgEngine;

/// <summary>
/// Genera il DWG finale:
///   1. Merge cartiglio nel layout (sovrascrive blocchi esistenti)
///   2. Scrittura attributi CASE-INSENSITIVE + supporto MText multilinea
///   3. Salvataggio
///
/// FIX v3 (2026-06-05):
///   - Evita doppio clone: BlockRecord aggiunti al documento DOPO la creazione degli Insert
///   - Insert.Block e' lo STESSO oggetto del BlockRecord nel documento
///   - MText preservati correttamente nella definizione del blocco
///
/// FIX v4 (2026-06-08):
///   - Fix MText AlignmentPoint: se (0,0,0) dopo clone, imposta (1,0,0)
///   - Rimossa doppia iterazione in FixAllReferences e RemoveInsertsForBlock
///   - Catch vuoti sostituiti con log di warning
///   - WriteAttributes retrocompatibile chiama WriteAllAttributesCaseInsensitive
///
/// FIX v5b (2026-06-08):
///   - Workaround DwgWriter ACadSharp: MText dentro BlockRecord importati non scritti nel DWG.
///     Soluzione: MText estratti, trasformati in coordinate mondo, aggiunti come standalone.
///   - MText creati da zero (new MText) invece di Clone() per evitare handle/owner rotti
///   - MText.Rotation e' read-only: rotazione applicata ruotando il direction vector AlignmentPoint
///
/// FIX v6 (2026-06-26):
///   - A0: coordinate cartiglio X=8768.0006 Y=0
///   - A0: l'Insert viene portato alla coordinata target e gli attributi al suo interno
///         vengono traslati dello STESSO delta (movimento relativo).
///
/// FIX v7 (2026-06-26):
///   - "Drawing needs recovery": ACadSharp DwgWriter produce file che AutoCAD chiede
///     di ricoverare quando la versione del documento e' inferiore ad AC1018.
///     Soluzione: prima della scrittura la versione viene portata ad AC1032 (>= AC1018),
///     un formato stabile e supportato dal DwgWriter. (ref. ACadSharp issue #956)
/// </summary>
public static class DwgWriter
{
    // Coordinata di destinazione del cartiglio in formato A0.
    private const double A0_X = 8768.0006;
    private const double A0_Y = 0.0;

    // Versione minima che evita il "drawing needs recovery" di ACadSharp (issue #956).
    // AC1021 NON e' supportato in scrittura -> si usa AC1032 (stabile, AutoCAD 2018+).
    private const ACadVersion SafeWriteVersion = ACadVersion.AC1032;

    public static string GenerateOutput(
        string layoutPath,
        string cartiglioPath,
        string outputPath,
        Dictionary<string, string> allAttributes,
        string cartiglioFormat = "A1")
    {
        var log = new StringBuilder();
        log.AppendLine("=== DWG Generation Log ===");
        log.AppendLine($"Layout:    {Path.GetFileName(layoutPath)}");
        log.AppendLine($"Cartiglio: {Path.GetFileName(cartiglioPath)}");
        log.AppendLine($"Output:    {Path.GetFileName(outputPath)}");
        var normalizedFormat = NormalizeCartiglioFormat(cartiglioFormat);
        allAttributes["FORMATTEXT"] = normalizedFormat;
        log.AppendLine($"Formato cartiglio: {normalizedFormat}");
        log.AppendLine($"Attributi totali nel dizionario: {allAttributes.Count}");
        log.AppendLine();
        log.AppendLine("[1] Apertura documenti...");
        var layoutDoc = DwgReader.Open(layoutPath);
        var cartiglioDoc = DwgReader.Open(cartiglioPath);
        log.AppendLine("OK");
        log.AppendLine();
        log.AppendLine("[2] Merge cartiglio nel layout...");
        var mergeLog = MergeDocuments(layoutDoc, cartiglioDoc, normalizedFormat == "A0");
        log.Append(mergeLog);
        log.AppendLine();
        log.AppendLine("[3] Scrittura attributi (case-insensitive)...");
        var writeLog = WriteAllAttributesCaseInsensitive(layoutDoc, allAttributes);
        log.Append(writeLog);
        log.AppendLine();
        log.AppendLine("[4] Salvataggio...");
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // FIX v7: evita il "drawing needs recovery" forzando una versione >= AC1018.
        var originalVersion = layoutDoc.Header.Version;
        if (originalVersion < SafeWriteVersion)
        {
            layoutDoc.Header.Version = SafeWriteVersion;
            log.AppendLine($"[Version] Versione DWG portata da {originalVersion} a {SafeWriteVersion} (evita recovery)");
        }
        else
        {
            log.AppendLine($"[Version] Versione DWG mantenuta: {originalVersion}");
        }

        using var writer = new ACadSharp.IO.DwgWriter(outputPath, layoutDoc);
        writer.Write();
        log.AppendLine($"File salvato: {outputPath}");
        log.AppendLine();
        log.AppendLine("=== COMPLETATO ===");
        return log.ToString();
    }
    // ──────────────────────────────────────────────
    //  HELPER: Fix MText defaults (AlignmentPoint)
    // ──────────────────────────────────────────────
    private static bool FixMTextDefaults(MText mtext, StringBuilder? log = null)
    {
        if (mtext == null) return false;
        bool fixed_ = false;
        if (mtext.AlignmentPoint == default)
        {
            mtext.AlignmentPoint = new CSMath.XYZ(1, 0, 0);
            fixed_ = true;
            string preview = string.IsNullOrEmpty(mtext.Value)
                ? "(vuoto)"
                : mtext.Value.Length > 40 ? mtext.Value.Substring(0, 40) + "..." : mtext.Value;
            log?.AppendLine($"  [FixMText] AlignmentPoint corretto (0,0,0)->(1,0,0) per MText '{preview}'");
        }
        return fixed_;
    }
    // ──────────────────────────────────────────────
    //  HELPER: Crea MText da zero con trasformazione
    // ──────────────────────────────────────────────
    /// <summary>
    /// Crea un NUOVO MText (senza Clone!) e lo trasforma nello spazio mondo
    /// applicando posizione, scala e rotazione dell'Insert.
    /// Creare l'MText da zero evita handle/owner rotti dal documento sorgente.
    /// </summary>
    private static MText TransformMText(MText source, Insert insert, CadDocument destDoc)
    {
        var mt = new MText();
        mt.Value = source.Value ?? "";
        double cos = Math.Cos(insert.Rotation);
        double sin = Math.Sin(insert.Rotation);
        double ox = source.InsertPoint.X;
        double oy = source.InsertPoint.Y;
        double oz = source.InsertPoint.Z;
        double sx = ox * insert.XScale;
        double sy = oy * insert.YScale;
        double wx = insert.InsertPoint.X + sx * cos - sy * sin;
        double wy = insert.InsertPoint.Y + sx * sin + sy * cos;
        double wz = insert.InsertPoint.Z + oz * insert.ZScale;
        mt.InsertPoint = new CSMath.XYZ(wx, wy, wz);
        double dirX = source.AlignmentPoint.X * cos - source.AlignmentPoint.Y * sin;
        double dirY = source.AlignmentPoint.X * sin + source.AlignmentPoint.Y * cos;
        double dirZ = source.AlignmentPoint.Z;
        mt.AlignmentPoint = new CSMath.XYZ(dirX, dirY, dirZ);
        mt.Height = source.Height * Math.Abs(insert.YScale);
        if (source.RectangleWidth > 0)
            mt.RectangleWidth = source.RectangleWidth * Math.Abs(insert.XScale);
        mt.AttachmentPoint = source.AttachmentPoint;
        mt.DrawingDirection = source.DrawingDirection;
        mt.LineSpacing = source.LineSpacing;
        mt.LineSpacingStyle = source.LineSpacingStyle;
        mt.Color = source.Color;
        mt.Normal = source.Normal;
        if (source.Style != null)
        {
            var style = destDoc.TextStyles.FirstOrDefault(s =>
                string.Equals(s.Name, source.Style.Name, StringComparison.OrdinalIgnoreCase));
            if (style != null) mt.Style = style;
        }
        if (source.Layer != null)
        {
            var layer = destDoc.Layers.FirstOrDefault(l =>
                string.Equals(l.Name, source.Layer.Name, StringComparison.OrdinalIgnoreCase));
            if (layer != null) mt.Layer = layer;
        }
        if (source.LineType != null)
        {
            var lt = destDoc.LineTypes.FirstOrDefault(l =>
                string.Equals(l.Name, source.LineType.Name, StringComparison.OrdinalIgnoreCase));
            if (lt != null) mt.LineType = lt;
        }
        return mt;
    }
    private static string NormalizeCartiglioFormat(string? format)
    {
        var value = (format ?? string.Empty).Trim().ToUpperInvariant();
        return value == "A0" ? "A0" : "A1";
    }
    /// <summary>
    /// Porta l'Insert del cartiglio alla posizione A0 (X=8768.0006, Y=0) e trasla
    /// dello STESSO delta anche gli attributi dell'Insert. Gli attributi hanno
    /// coordinate proprie: senza questa traslazione resterebbero nella posizione
    /// originale pur facendo parte del blocco spostato (movimento relativo).
    /// </summary>
    private static void ApplyA0CartiglioPosition(Entity entity)
    {
        if (entity is not Insert insert) return;

        var oldPoint = insert.InsertPoint;
        var newPoint = new CSMath.XYZ(A0_X, A0_Y, oldPoint.Z);
        insert.InsertPoint = newPoint;

        double dx = newPoint.X - oldPoint.X;
        double dy = newPoint.Y - oldPoint.Y;
        double dz = newPoint.Z - oldPoint.Z;

        // Nessuno spostamento effettivo: niente da traslare.
        if (dx == 0 && dy == 0 && dz == 0) return;

        foreach (var att in insert.Attributes)
        {
            try
            {
                var ip = att.InsertPoint;
                att.InsertPoint = new CSMath.XYZ(ip.X + dx, ip.Y + dy, ip.Z + dz);

                var ap = att.AlignmentPoint;
                att.AlignmentPoint = new CSMath.XYZ(ap.X + dx, ap.Y + dy, ap.Z + dz);

                if (att.MText != null)
                {
                    var mip = att.MText.InsertPoint;
                    att.MText.InsertPoint = new CSMath.XYZ(mip.X + dx, mip.Y + dy, mip.Z + dz);
                }
            }
            catch { /* attributo senza geometria valida: ignora */ }
        }
    }
    // ──────────────────────────────────────────────
    //  MERGE
    // ──────────────────────────────────────────────
    private static string MergeDocuments(CadDocument dest, CadDocument source, bool moveCartiglioBlocksForA0)
    {
        var log = new StringBuilder();
        int imported = 0;
        if (moveCartiglioBlocksForA0)
            log.AppendLine($"[A0] Spostamento Insert del cartiglio a X={A0_X} Y={A0_Y}");
        // === PHASE 1: Import Layers, TextStyles, LineTypes ===
        try
        {
            foreach (var src in source.Layers)
            {
                if (dest.Layers.Any(l => string.Equals(l.Name, src.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                try { dest.Layers.Add((Layer)src.Clone()); log.AppendLine($"[Layer] '{src.Name}' importato"); imported++; }
                catch (Exception ex) { log.AppendLine($"[Layer] '{src.Name}' ERRORE: {ex.Message}"); }
            }
        }
        catch (Exception ex) { log.AppendLine($"[Layers] Errore: {ex.Message}"); }
        try
        {
            foreach (var src in source.TextStyles)
            {
                if (dest.TextStyles.Any(s => string.Equals(s.Name, src.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                try { dest.TextStyles.Add((TextStyle)src.Clone()); log.AppendLine($"[TextStyle] '{src.Name}' importato"); imported++; }
                catch (Exception ex) { log.AppendLine($"[TextStyle] '{src.Name}' ERRORE: {ex.Message}"); }
            }
        }
        catch (Exception ex) { log.AppendLine($"[TextStyles] Errore: {ex.Message}"); }
        try
        {
            foreach (var src in source.LineTypes)
            {
                if (dest.LineTypes.Any(l => string.Equals(l.Name, src.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                try { dest.LineTypes.Add((LineType)src.Clone()); log.AppendLine($"[LineType] '{src.Name}' importato"); imported++; }
                catch (Exception ex) { log.AppendLine($"[LineType] '{src.Name}' ERRORE: {ex.Message}"); }
            }
        }
        catch (Exception ex) { log.AppendLine($"[LineTypes] Errore: {ex.Message}"); }
        // === PHASE 2: Clone BlockRecords (pending, Document==null) ===
        var pendingBlocks = new List<(string Name, BlockRecord Block)>();
        try
        {
            foreach (var srcBr in source.BlockRecords)
            {
                if (srcBr.Name.StartsWith("*")) continue;
                var existingBr = dest.BlockRecords
                    .FirstOrDefault(b => string.Equals(b.Name, srcBr.Name, StringComparison.OrdinalIgnoreCase));
                if (existingBr != null)
                {
                    int removedInserts = RemoveInsertsForBlock(dest, existingBr.Name);
                    log.AppendLine($"[Block] '{existingBr.Name}' gia' presente: rimosso ({removedInserts} Insert eliminate)");
                    try { dest.BlockRecords.Remove(existingBr.Name); }
                    catch (Exception ex) { log.AppendLine($"[Block] '{existingBr.Name}' errore rimozione: {ex.Message}"); }
                }
                try
                {
                    var clonedBr = (BlockRecord)srcBr.Clone();
                    pendingBlocks.Add((srcBr.Name, clonedBr));
                    log.AppendLine($"[Block] '{srcBr.Name}' importato dal cartiglio");
                    imported++;
                    var mtextEntities = clonedBr.Entities.OfType<MText>().ToList();
                    if (mtextEntities.Count > 0)
                    {
                        log.AppendLine($"[MText] Blocco '{srcBr.Name}' contiene {mtextEntities.Count} MText:");
                        foreach (var mt in mtextEntities)
                        {
                            FixMTextDefaults(mt, log);
                            string preview = string.IsNullOrEmpty(mt.Value)
                                ? "(vuoto)"
                                : mt.Value.Length > 80 ? mt.Value.Substring(0, 80) + "..." : mt.Value;
                            log.AppendLine($"- Len={mt.Value?.Length ?? 0} Style='{mt.Style?.Name}' Text=\"{preview}\"");
                        }
                    }
                    var attrDefs = clonedBr.Entities.OfType<AttributeDefinition>().ToList();
                    if (attrDefs.Count > 0)
                    {
                        log.AppendLine($"[AttrDef] Blocco '{srcBr.Name}' contiene {attrDefs.Count} AttributeDefinition:");
                        foreach (var ad in attrDefs)
                        {
                            if (ad.MText != null) FixMTextDefaults(ad.MText, log);
                            string hasMt = ad.MText != null ? $" +MText(len={ad.MText.Value?.Length ?? 0})" : "";
                            log.AppendLine($"- Tag='{ad.Tag}' Value='{ad.Value}'{hasMt}");
                        }
                    }
                }
                catch (Exception ex) { log.AppendLine($"[Block] '{srcBr.Name}' ERRORE clone: {ex.Message}"); }
            }
        }
        catch (Exception ex) { log.AppendLine($"[BlockRecords] Errore: {ex.Message}"); }
        // === PHASE 3+4: Import ModelSpace + PaperSpace ===
        var pendingModelSpaceEntities = new List<Entity>();
        var pendingPaperSpaceEntities = new List<Entity>();
        try
        {
            var srcEntities = source.ModelSpace.Entities.ToList();
            log.AppendLine($"[ModelSpace] {srcEntities.Count} entita' da importare...");
            int count = 0;
            foreach (var entity in srcEntities)
            {
                try
                {
                    if (entity is Insert srcInsert && srcInsert.Block != null)
                    {
                        var blockName = srcInsert.Block.Name;
                        BlockRecord? targetBr = pendingBlocks
                            .Where(pb => string.Equals(pb.Name, blockName, StringComparison.OrdinalIgnoreCase))
                            .Select(pb => pb.Block)
                            .FirstOrDefault();
                        if (targetBr == null)
                            targetBr = dest.BlockRecords
                                .FirstOrDefault(b => string.Equals(b.Name, blockName, StringComparison.OrdinalIgnoreCase));
                        if (targetBr != null)
                        {
                            var newInsert = CreateInsertWithAttributes(srcInsert, targetBr, dest, log);
                            if (moveCartiglioBlocksForA0) ApplyA0CartiglioPosition(newInsert);
                            bool sameObj = ReferenceEquals(newInsert.Block, targetBr);
                            int mtCount = newInsert.Block?.Entities.OfType<MText>().Count() ?? 0;
                            log.AppendLine($"  [Verify] Insert.Block==targetBr: {sameObj}, MText: {mtCount}");
                            pendingModelSpaceEntities.Add(newInsert);
                            count++;
                        }
                        else
                        {
                            var clonedEntity = (Entity)entity.Clone();
                            if (moveCartiglioBlocksForA0) ApplyA0CartiglioPosition(clonedEntity);
                            pendingModelSpaceEntities.Add(clonedEntity);
                            count++;
                        }
                    }
                    else
                    {
                        pendingModelSpaceEntities.Add((Entity)entity.Clone());
                        count++;
                    }
                }
                catch (Exception ex) { log.AppendLine($"[ModelSpace] Errore {entity.GetType().Name}: {ex.Message}"); }
            }
            log.AppendLine($"[ModelSpace] {count} entita' importate");
            imported += count;
        }
        catch (Exception ex) { log.AppendLine($"[ModelSpace] Errore: {ex.Message}"); }
        try
        {
            if (source.PaperSpace != null)
            {
                var srcEntities = source.PaperSpace.Entities.ToList();
                log.AppendLine($"[PaperSpace] {srcEntities.Count} entita' da importare...");
                int count = 0;
                foreach (var entity in srcEntities)
                {
                    try
                    {
                        if (entity is Insert srcInsert && srcInsert.Block != null)
                        {
                            var blockName = srcInsert.Block.Name;
                            BlockRecord? targetBr = pendingBlocks
                                .Where(pb => string.Equals(pb.Name, blockName, StringComparison.OrdinalIgnoreCase))
                                .Select(pb => pb.Block)
                                .FirstOrDefault();
                            if (targetBr == null)
                                targetBr = dest.BlockRecords
                                    .FirstOrDefault(b => string.Equals(b.Name, blockName, StringComparison.OrdinalIgnoreCase));
                            if (targetBr != null)
                            {
                                var newInsert = CreateInsertWithAttributes(srcInsert, targetBr, dest, log);
                                if (moveCartiglioBlocksForA0) ApplyA0CartiglioPosition(newInsert);
                                pendingPaperSpaceEntities.Add(newInsert);
                                count++;
                            }
                            else
                            {
                                var clonedEntity = (Entity)entity.Clone();
                                if (moveCartiglioBlocksForA0) ApplyA0CartiglioPosition(clonedEntity);
                                pendingPaperSpaceEntities.Add(clonedEntity);
                                count++;
                            }
                        }
                        else
                        {
                            pendingPaperSpaceEntities.Add((Entity)entity.Clone());
                            count++;
                        }
                    }
                    catch (Exception ex) { log.AppendLine($"[PaperSpace] Errore {entity.GetType().Name}: {ex.Message}"); }
                }
                log.AppendLine($"[PaperSpace] {count} entita' importate");
                imported += count;
            }
        }
        catch (Exception ex) { log.AppendLine($"[PaperSpace] Errore: {ex.Message}"); }
        // === PHASE 5: Aggiungi BlockRecords al documento ===
        log.AppendLine("[Phase5] Aggiunta BlockRecords al documento...");
        foreach (var pending in pendingBlocks)
        {
            try
            {
                dest.BlockRecords.Add(pending.Block);
                int mtCount = pending.Block.Entities.OfType<MText>().Count();
                log.AppendLine($"  '{pending.Name}' aggiunto (MText: {mtCount})");
            }
            catch (Exception ex) { log.AppendLine($"  '{pending.Name}' ERRORE: {ex.Message}"); }
        }
        // === PHASE 5b: Aggiungi entita' a ModelSpace/PaperSpace ===
        log.AppendLine("[Phase5b] Aggiunta entita' a ModelSpace/PaperSpace...");
        int msAdded = 0, psAdded = 0;
        foreach (var ent in pendingModelSpaceEntities)
        {
            try { dest.ModelSpace.Entities.Add(ent); msAdded++; }
            catch (Exception ex) { log.AppendLine($"  [ModelSpace] Errore Add {ent.GetType().Name}: {ex.Message}"); }
        }
        log.AppendLine($"  ModelSpace: {msAdded} entita' aggiunte");
        foreach (var ent in pendingPaperSpaceEntities)
        {
            try
            {
                if (dest.PaperSpace != null)
                {
                    dest.PaperSpace.Entities.Add(ent);
                }
                else
                {
                    log.AppendLine($"  [WARN] PaperSpace null: {ent.GetType().Name} aggiunta in ModelSpace");
                    dest.ModelSpace.Entities.Add(ent);
                }
                psAdded++;
            }
            catch (Exception ex) { log.AppendLine($"  [PaperSpace] Errore Add {ent.GetType().Name}: {ex.Message}"); }
        }
        if (psAdded > 0) log.AppendLine($"  PaperSpace: {psAdded} entita' aggiunte");
        // === PHASE 6: Fix references ===
        try
        {
            FixAllReferences(dest, log);
            log.AppendLine("[Fix] Riferimenti TextStyle/Layer/LineType risolti su tutte le entita'");
        }
        catch (Exception ex) { log.AppendLine($"[Fix] Errore: {ex.Message}"); }
        // === PHASE 6b: WORKAROUND - Estrazione MText dai blocchi importati ===
        log.AppendLine();
        log.AppendLine("[Phase6b] Workaround MText: estrazione da blocchi importati...");
        var importedBlockNames = new HashSet<string>(
            pendingBlocks.Select(pb => pb.Name),
            StringComparer.OrdinalIgnoreCase);
        var extractedMTexts = new List<(BlockRecord TargetSpace, MText TransformedMText)>();
        int totalExtracted = 0;
        foreach (var layout in dest.Layouts)
        {
            if (layout.AssociatedBlock == null) continue;
            var space = layout.AssociatedBlock;
            foreach (var insert in space.Entities.OfType<Insert>().ToList())
            {
                if (insert.Block == null) continue;
                if (!importedBlockNames.Contains(insert.Block.Name)) continue;
                var mtexts = insert.Block.Entities.OfType<MText>().ToList();
                if (mtexts.Count == 0) continue;
                log.AppendLine($"  [{layout.Name}] Insert->'{insert.Block.Name}': {mtexts.Count} MText da estrarre");
                log.AppendLine($"    InsertPoint=({insert.InsertPoint.X:F2},{insert.InsertPoint.Y:F2},{insert.InsertPoint.Z:F2}) " +
                               $"Scale=({insert.XScale:F3},{insert.YScale:F3},{insert.ZScale:F3}) Rot={insert.Rotation:F4} rad");
                foreach (var srcMt in mtexts)
                {
                    try
                    {
                        var transformed = TransformMText(srcMt, insert, dest);
                        FixMTextDefaults(transformed, log);
                        extractedMTexts.Add((space, transformed));
                        totalExtracted++;
                        string preview = string.IsNullOrEmpty(srcMt.Value)
                            ? "(vuoto)"
                            : srcMt.Value.Length > 60 ? srcMt.Value.Substring(0, 60) + "..." : srcMt.Value;
                        log.AppendLine($"    [OK] MText estratto -> ({transformed.InsertPoint.X:F2},{transformed.InsertPoint.Y:F2}) " +
                                       $"H={transformed.Height:F2} Style='{transformed.Style?.Name}' Text=\"{preview}\"");
                    }
                    catch (Exception ex)
                    {
                        string preview = string.IsNullOrEmpty(srcMt.Value)
                            ? "(vuoto)"
                            : srcMt.Value.Length > 40 ? srcMt.Value.Substring(0, 40) + "..." : srcMt.Value;
                        log.AppendLine($"    [ERR] MText \"{preview}\": {ex.Message}");
                    }
                }
            }
        }
        // Rimuovi MText dai BlockRecord importati (1 volta per blocco)
        var cleanedBlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pending in pendingBlocks)
        {
            if (cleanedBlocks.Contains(pending.Name)) continue;
            var br = dest.BlockRecords
                .FirstOrDefault(b => string.Equals(b.Name, pending.Name, StringComparison.OrdinalIgnoreCase));
            if (br == null) continue;
            var mtextsToRemove = br.Entities.OfType<MText>().ToList();
            if (mtextsToRemove.Count == 0) continue;
            int removedCount = 0;
            foreach (var mt in mtextsToRemove)
            {
                try { br.Entities.Remove(mt); removedCount++; }
                catch (Exception ex) { log.AppendLine($"  [WARN] Rimozione MText da '{pending.Name}': {ex.Message}"); }
            }
            log.AppendLine($"  [Clean] BlockRecord '{pending.Name}': {removedCount} MText rimossi dalla definizione");
            cleanedBlocks.Add(pending.Name);
        }
        // Aggiungi MText standalone
        int addedCount = 0;
        foreach (var (targetSpace, mt) in extractedMTexts)
        {
            try { targetSpace.Entities.Add(mt); addedCount++; }
            catch (Exception ex) { log.AppendLine($"  [ERR] Aggiunta MText standalone: {ex.Message}"); }
        }
        log.AppendLine($"[Phase6b] Totale: {totalExtracted} MText estratti, {addedCount} aggiunti come standalone");
        log.AppendLine();
        // === PHASE 7: Verifica finale ===
        log.AppendLine("[Verify] Verifica finale MText:");
        foreach (var br in dest.BlockRecords)
        {
            int mtCount = br.Entities.OfType<MText>().Count();
            if (mtCount > 0)
                log.AppendLine($"  BlockRecord '{br.Name}': {mtCount} MText");
        }
        foreach (var ins in dest.ModelSpace.Entities.OfType<Insert>())
        {
            if (ins.Block != null)
            {
                int mtCount = ins.Block.Entities.OfType<MText>().Count();
                if (mtCount > 0)
                {
                    var brInDoc = dest.BlockRecords
                        .FirstOrDefault(b => string.Equals(b.Name, ins.Block.Name, StringComparison.OrdinalIgnoreCase));
                    bool sameObj = brInDoc != null && ReferenceEquals(ins.Block, brInDoc);
                    log.AppendLine($"  Insert->'{ins.Block.Name}': {mtCount} MText, SameObjAsDoc: {sameObj}");
                }
            }
        }
        int standaloneMs = dest.ModelSpace.Entities.OfType<MText>().Count();
        log.AppendLine($"  MText standalone in *Model_Space: {standaloneMs}");
        if (dest.PaperSpace != null)
        {
            int standalonePs = dest.PaperSpace.Entities.OfType<MText>().Count();
            if (standalonePs > 0) log.AppendLine($"  MText standalone in *Paper_Space: {standalonePs}");
        }
        log.AppendLine($"Totale importati: {imported}");
        return log.ToString();
    }
    // ──────────────────────────────────────────────
    //  CREATE INSERT WITH ATTRIBUTES
    // ──────────────────────────────────────────────
    private static Insert CreateInsertWithAttributes(
        Insert srcInsert, BlockRecord targetBr, CadDocument dest, StringBuilder log)
    {
        var newInsert = new Insert(targetBr);
        newInsert.InsertPoint = srcInsert.InsertPoint;
        newInsert.XScale = srcInsert.XScale;
        newInsert.YScale = srcInsert.YScale;
        newInsert.ZScale = srcInsert.ZScale;
        newInsert.Rotation = srcInsert.Rotation;
        if (srcInsert.Layer != null)
        {
            var destLayer = dest.Layers.FirstOrDefault(l =>
                string.Equals(l.Name, srcInsert.Layer.Name, StringComparison.OrdinalIgnoreCase));
            if (destLayer != null) newInsert.Layer = destLayer;
        }
        var blockName = srcInsert.Block?.Name ?? "(null)";
        var srcAttrs = srcInsert.Attributes.ToList();
        var destAttrs = newInsert.Attributes.ToList();
        int copiedCount = 0;
        foreach (var srcAtt in srcAttrs)
        {
            if (string.IsNullOrWhiteSpace(srcAtt.Tag)) continue;
            var destAtt = destAttrs.FirstOrDefault(a =>
                string.Equals(a.Tag, srcAtt.Tag, StringComparison.OrdinalIgnoreCase));
            if (destAtt != null)
            {
                destAtt.Value = srcAtt.Value;
                try
                {
                    if (srcAtt.MText != null)
                    {
                        if (destAtt.MText != null)
                            destAtt.MText.Value = srcAtt.MText.Value;
                        else
                            destAtt.MText = (MText)srcAtt.MText.Clone();
                        if (destAtt.MText != null)
                            FixMTextDefaults(destAtt.MText, log);
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine($"  [WARN] Block '{blockName}' Attr '{srcAtt.Tag}' MText copy: {ex.Message}");
                }
                try
                {
                    destAtt.InsertPoint = srcAtt.InsertPoint;
                    destAtt.Height = srcAtt.Height;
                    destAtt.Rotation = srcAtt.Rotation;
                    destAtt.Thickness = srcAtt.Thickness;
                }
                catch (Exception ex)
                {
                    log.AppendLine($"  [WARN] Block '{blockName}' Attr '{srcAtt.Tag}' proprieta' geometriche: {ex.Message}");
                }
                copiedCount++;
            }
            else
            {
                log.AppendLine($"  [WARN] Block '{blockName}' Attr '{srcAtt.Tag}' non trovato nel nuovo Insert");
            }
        }
        if (srcAttrs.Count > 0)
            log.AppendLine($"  [Attrs] Block '{blockName}': {copiedCount}/{srcAttrs.Count} copiati (dest: {destAttrs.Count})");
        return newInsert;
    }
    // ──────────────────────────────────────────────
    //  FIX ALL REFERENCES
    // ──────────────────────────────────────────────
    private static void FixAllReferences(CadDocument dest, StringBuilder log)
    {
        void FixEntity(Entity ent)
        {
            try
            {
                if (ent is MText mtext)
                {
                    FixMTextDefaults(mtext);
                    if (mtext.Style != null)
                    {
                        var style = dest.TextStyles.FirstOrDefault(s =>
                            string.Equals(s.Name, mtext.Style.Name, StringComparison.OrdinalIgnoreCase));
                        if (style != null && !ReferenceEquals(mtext.Style, style))
                            mtext.Style = style;
                    }
                }
                else if (ent is TextEntity text && text.Style != null)
                {
                    var style = dest.TextStyles.FirstOrDefault(s =>
                        string.Equals(s.Name, text.Style.Name, StringComparison.OrdinalIgnoreCase));
                    if (style != null && !ReferenceEquals(text.Style, style))
                        text.Style = style;
                }
                if (ent.Layer != null)
                {
                    var layer = dest.Layers.FirstOrDefault(l =>
                        string.Equals(l.Name, ent.Layer.Name, StringComparison.OrdinalIgnoreCase));
                    if (layer != null && !ReferenceEquals(ent.Layer, layer))
                        ent.Layer = layer;
                }
                if (ent.LineType != null)
                {
                    var lt = dest.LineTypes.FirstOrDefault(l =>
                        string.Equals(l.Name, ent.LineType.Name, StringComparison.OrdinalIgnoreCase));
                    if (lt != null && !ReferenceEquals(ent.LineType, lt))
                        ent.LineType = lt;
                }
                if (ent is Insert insert)
                {
                    foreach (var att in insert.Attributes)
                    {
                        FixEntity(att);
                        try
                        {
                            if (att.MText != null)
                            {
                                FixMTextDefaults(att.MText);
                                FixEntity(att.MText);
                            }
                        }
                        catch (Exception ex) { log.AppendLine($"  [WARN] FixEntity Attr MText: {ex.Message}"); }
                    }
                    if (insert.Block != null)
                        foreach (var blockEnt in insert.Block.Entities)
                            FixEntity(blockEnt);
                }
            }
            catch (Exception ex) { log.AppendLine($"  [WARN] FixEntity {ent.GetType().Name}: {ex.Message}"); }
        }
        foreach (var br in dest.BlockRecords)
            foreach (var ent in br.Entities)
                FixEntity(ent);
    }
    // ──────────────────────────────────────────────
    //  REMOVE INSERTS FOR BLOCK
    // ──────────────────────────────────────────────
    private static int RemoveInsertsForBlock(CadDocument doc, string blockName)
    {
        int removed = 0;
        foreach (var layout in doc.Layouts)
        {
            if (layout.AssociatedBlock == null) continue;
            var inserts = layout.AssociatedBlock.Entities.OfType<Insert>()
                .Where(i => string.Equals(i.Block?.Name, blockName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var ins in inserts)
            {
                try { layout.AssociatedBlock.Entities.Remove(ins); removed++; }
                catch { /* Entita' gia' rimossa */ }
            }
        }
        return removed;
    }
    // ──────────────────────────────────────────────
    //  WRITE ATTRIBUTES (CASE-INSENSITIVE)
    // ──────────────────────────────────────────────
    private static string WriteAllAttributesCaseInsensitive(
        CadDocument doc,
        Dictionary<string, string> attributes)
    {
        var log = new StringBuilder();
        var ciNormal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ciRev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in attributes)
        {
            if (kv.Key.StartsWith("REV_", StringComparison.OrdinalIgnoreCase))
                ciRev[kv.Key.Substring(4)] = kv.Value;
            else
                ciNormal[kv.Key] = kv.Value;
        }
        var ciLookup = new Dictionary<string, (string OriginalKey, string Value)>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in ciNormal) ciLookup[kv.Key] = (kv.Key, kv.Value);
        foreach (var kv in ciRev) ciLookup[kv.Key] = ("REV_" + kv.Key, kv.Value);
        int totalWritten = 0;
        var matchedDwgTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allDwgTags = new List<(string BlockName, string Tag, string Source)>();
        var blockStats = new Dictionary<string, (int Found, int Written)>(StringComparer.OrdinalIgnoreCase);
        void ProcessInsert(Insert insert, string source)
        {
            var blockName = insert.Block?.Name ?? "(null)";
            var attrList = insert.Attributes.ToList();
            if (attrList.Count == 0) return;
            if (!blockStats.ContainsKey(blockName))
                blockStats[blockName] = (0, 0);
            var stats = blockStats[blockName];
            stats.Found += attrList.Count;
            log.AppendLine($"[{source}] Blocco \"{blockName}\" ({attrList.Count} attributi):");
            foreach (var att in attrList)
            {
                allDwgTags.Add((blockName, att.Tag, source));
                bool isRevBlock = blockName.Contains("revision", StringComparison.OrdinalIgnoreCase);
                var primaryDict = isRevBlock ? ciRev : ciNormal;
                var fallbackDict = isRevBlock ? ciNormal : ciRev;
                string? newValue = null;
                bool hasMatch = primaryDict.TryGetValue(att.Tag, out newValue) || fallbackDict.TryGetValue(att.Tag, out newValue);
                if (hasMatch)
                {
                    if (newValue == null) newValue = "";
                    var oldValue = att.Value ?? "";
                    var plainValue = newValue.Replace("\r\n", " ").Replace("\n", " ");
                    var mtextValue = newValue.Replace("\r\n", "\\P").Replace("\n", "\\P");
                    att.Value = plainValue;
                    bool mtextUpdated = false;
                    try
                    {
                        if (att.MText != null)
                        {
                            att.MText.Value = mtextValue;
                            FixMTextDefaults(att.MText);
                            mtextUpdated = true;
                        }
                    }
                    catch (Exception ex) { log.AppendLine($"  [WARN] MText update '{att.Tag}': {ex.Message}"); }
                    matchedDwgTags.Add(att.Tag);
                    totalWritten++;
                    stats.Written++;
                    string mtextNote = mtextUpdated ? " +MText" : "";
                    log.AppendLine($"[OK] {att.Tag}: \"{oldValue}\" -> \"{newValue}\"{mtextNote}");
                }
                else
                {
                    log.AppendLine($"[--] {att.Tag}: \"{att.Value}\" (NESSUN MATCH)");
                }
            }
            blockStats[blockName] = stats;
        }
        foreach (var insert in doc.ModelSpace.Entities.OfType<Insert>())
            ProcessInsert(insert, "ModelSpace");
        if (doc.PaperSpace != null)
            foreach (var insert in doc.PaperSpace.Entities.OfType<Insert>())
                ProcessInsert(insert, "PaperSpace");
        foreach (var layout in doc.Layouts)
        {
            if (layout.AssociatedBlock == null) continue;
            foreach (var insert in layout.AssociatedBlock.Entities.OfType<Insert>())
                ProcessInsert(insert, $"Layout:{layout.Name}");
        }
        log.AppendLine();
        log.AppendLine("=== RIEPILOGO ===");
        log.AppendLine($"Attributi scritti totali: {totalWritten}");
        log.AppendLine();
        log.AppendLine("Per blocco:");
        foreach (var kv in blockStats.OrderBy(x => x.Key))
            log.AppendLine($"\"{kv.Key}\": {kv.Value.Written}/{kv.Value.Found} attributi scritti");
        var dictKeysNotInDwg = attributes.Keys
            .Where(k => !matchedDwgTags.Contains(k) && !string.IsNullOrEmpty(attributes[k]))
            .OrderBy(k => k).ToList();
        if (dictKeysNotInDwg.Count > 0)
        {
            log.AppendLine();
            log.AppendLine($"Tag con VALORE nel dizionario ma NON trovati nel DWG ({dictKeysNotInDwg.Count}):");
            foreach (var tag in dictKeysNotInDwg)
                log.AppendLine($"- \"{tag}\" = \"{attributes[tag]}\"");
        }
        var legendTags = allDwgTags
            .Where(t => string.Equals(t.BlockName, "LegendBlock", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Tag).Distinct().ToList();
        if (legendTags.Count > 0)
        {
            log.AppendLine();
            log.AppendLine("=== CONFRONTO TAG LegendBlock ===");
            log.AppendLine($"{"Tag nel DWG",-35} {"Tag nel Dizionario",-35} {"Match?"}");
            log.AppendLine(new string('-', 80));
            foreach (var dwgTag in legendTags.OrderBy(t => t))
            {
                if (ciLookup.TryGetValue(dwgTag, out var m))
                {
                    string cm = dwgTag == m.OriginalKey ? "ESATTO" : "CASE DIVERSO";
                    log.AppendLine($"{dwgTag,-35} {m.OriginalKey,-35} {cm}");
                }
                else
                    log.AppendLine($"{dwgTag,-35} {"(NON TROVATO)",-35} NO");
            }
        }
        var titleTags = allDwgTags
            .Where(t => string.Equals(t.BlockName, "titleblock", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Tag).Distinct().ToList();
        if (titleTags.Count > 0)
        {
            log.AppendLine();
            log.AppendLine("=== CONFRONTO TAG titleblock ===");
            log.AppendLine($"{"Tag nel DWG",-35} {"Tag nel Dizionario",-35} {"Match?"}");
            log.AppendLine(new string('-', 80));
            foreach (var dwgTag in titleTags.OrderBy(t => t))
            {
                if (ciLookup.TryGetValue(dwgTag, out var m))
                {
                    string cm = dwgTag == m.OriginalKey ? "ESATTO" : "CASE DIVERSO";
                    log.AppendLine($"{dwgTag,-35} {m.OriginalKey,-35} {cm}");
                }
                else
                    log.AppendLine($"{dwgTag,-35} {"(NON TROVATO)",-35} NO");
            }
        }
        return log.ToString();
    }
    // ──────────────────────────────────────────────
    //  WRITE ATTRIBUTES (OVERLOAD RETROCOMPATIBILE)
    // ──────────────────────────────────────────────
    public static void WriteAttributes(string templatePath, string outputPath,
        Dictionary<string, string> attributes)
    {
        var doc = DwgReader.Open(templatePath);
        _ = WriteAllAttributesCaseInsensitive(doc, attributes);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // FIX v7: evita il "drawing needs recovery" forzando una versione >= AC1018.
        if (doc.Header.Version < SafeWriteVersion)
            doc.Header.Version = SafeWriteVersion;

        using var writer = new ACadSharp.IO.DwgWriter(outputPath, doc);
        writer.Write();
    }
}
