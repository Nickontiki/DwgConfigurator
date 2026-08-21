using System.Text;
using ACadSharp;
using ACadSharp.Entities;

namespace DwgConfigurator.Shared.DwgEngine;

public static class DwgReader
{
    public static CadDocument Open(string dwgPath)
    {
        using var reader = new ACadSharp.IO.DwgReader(dwgPath);
        return reader.Read();
    }

    public static string GetDiagnostics(string dwgPath)
    {
        var doc = Open(dwgPath);
        var sb = new StringBuilder();

        sb.AppendLine($"=== Diagnostica DWG: {Path.GetFileName(dwgPath)} ===");
        sb.AppendLine();

        void DumpInserts(IEnumerable<Insert> inserts, string source)
        {
            int count = 0;
            foreach (var insert in inserts)
            {
                var blockName = insert.Block?.Name ?? "(null)";
                var attrCount = insert.Attributes.Count();
                sb.AppendLine($"  [{source}] Blocco: \"{blockName}\" ({attrCount} attributi)");
                foreach (var att in insert.Attributes)
                    sb.AppendLine($"    Tag: \"{att.Tag}\" = \"{att.Value}\"");
                count++;
            }
            if (count == 0) sb.AppendLine($"  [{source}] (nessun Insert trovato)");
        }

        sb.AppendLine("--- ModelSpace ---");
        DumpInserts(doc.ModelSpace.Entities.OfType<Insert>(), "ModelSpace");

        sb.AppendLine();
        sb.AppendLine("--- PaperSpace ---");
        if (doc.PaperSpace != null)
            DumpInserts(doc.PaperSpace.Entities.OfType<Insert>(), "PaperSpace");
        else
            sb.AppendLine("  (PaperSpace null)");

        sb.AppendLine();
        sb.AppendLine("--- Layouts ---");
        foreach (var layout in doc.Layouts)
        {
            sb.AppendLine($"  Layout: \"{layout.Name}\"");
            if (layout.AssociatedBlock == null) { sb.AppendLine("    (no block)"); continue; }
            DumpInserts(layout.AssociatedBlock.Entities.OfType<Insert>(), $"Layout:{layout.Name}");
        }

        sb.AppendLine();
        sb.AppendLine("--- BlockRecords ---");
        foreach (var br in doc.BlockRecords)
        {
            if (br.Name.StartsWith("*")) continue;
            sb.AppendLine($"  \"{br.Name}\" ({br.Entities.Count()} entities)");
        }

        return sb.ToString();
    }

    public static Dictionary<string, string> ExtractAttributes(string dwgPath, string[]? allowedTags = null)
    {
        var doc = Open(dwgPath);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allowedSet = allowedTags != null ? new HashSet<string>(allowedTags, StringComparer.OrdinalIgnoreCase) : null;

        void ExtractFromInserts(IEnumerable<Insert> inserts)
        {
            foreach (var insert in inserts)
            {
                foreach (var att in insert.Attributes)
                {
                    if (!string.IsNullOrWhiteSpace(att.Tag))
                    {
                        if (allowedSet != null && !allowedSet.Contains(att.Tag)) continue;

                        if (!result.ContainsKey(att.Tag) || string.IsNullOrEmpty(result[att.Tag]))
                        {
                            var val = att.Value ?? string.Empty;
                            try { if (att.MText != null && !string.IsNullOrEmpty(att.MText.Value)) val = att.MText.Value; } catch { }
                            val = val.Replace("\\P", "\r\n").Replace("\\p", "\r\n");
                            result[att.Tag] = val;
                        }
                    }
                }
            }
        }

        ExtractFromInserts(doc.ModelSpace.Entities.OfType<Insert>());
        if (doc.PaperSpace != null)
            ExtractFromInserts(doc.PaperSpace.Entities.OfType<Insert>());

        foreach (var layout in doc.Layouts)
        {
            if (layout.AssociatedBlock != null)
                ExtractFromInserts(layout.AssociatedBlock.Entities.OfType<Insert>());
        }

        foreach (var br in doc.BlockRecords)
        {
            if (br.Name.StartsWith("*")) continue;
            foreach (var ent in br.Entities.OfType<AttributeDefinition>())
            {
                if (!string.IsNullOrWhiteSpace(ent.Tag))
                {
                    if (allowedSet != null && !allowedSet.Contains(ent.Tag)) continue;

                    if (!result.ContainsKey(ent.Tag) || string.IsNullOrEmpty(result[ent.Tag]))
                    {
                        var val = ent.Value ?? string.Empty;
                        try { if (ent.MText != null && !string.IsNullOrEmpty(ent.MText.Value)) val = ent.MText.Value; } catch { }
                        val = val.Replace("\\P", "\r\n").Replace("\\p", "\r\n");
                        result[ent.Tag] = val;
                    }
                }
            }
        }

        return result;
    }
}
