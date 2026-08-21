using System.IO;

namespace DwgConfigurator.Shared.Config;

/// <summary>
/// Rende portabili i percorsi DWG tra PC/utenti diversi.
///
/// Problema: la stessa libreria SharePoint viene sincronizzata con root diverse, es.
///   nicola : C:\Users\nicola.festa\OneDrive - GruppoAB\Industrial Electrical Design Engineering - LAYOUT\Layout 2.0\...
///   claudio: C:\Users\claudio.boglioli\GruppoAB\Industrial Electrical Design Engineering - Documenti\LAYOUT\Layout 2.0\...
///
/// L'unica parte identica e' cio' che sta DOPO la cartella LAYOUT.
/// Percio' salviamo il percorso relativo a partire da li e lo risolviamo
/// sulla root LAYOUT locale della macchina corrente.
/// </summary>
public static class PortablePathResolver
{
    private const StringComparison OIC = StringComparison.OrdinalIgnoreCase;
    private const string SiteMarker = "Industrial Electrical Design Engineering";

    // Override manuale opzionale (punta alla cartella che contiene "Layout 2.0", cioe' la root LAYOUT).
    private const string LayoutRootEnvVar = "DWGCONFIGURATOR_LAYOUT_ROOT";
    private const string LibraryRootEnvVar = "DWGCONFIGURATOR_LIBRARY_ROOT";

    // ──────────────────────────────────────────────────────────────
    //  SALVATAGGIO: assoluto -> relativo portabile
    // ──────────────────────────────────────────────────────────────
    public static string ToPortablePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        path = Environment.ExpandEnvironmentVariables(path.Trim());

        // Gia' relativo: normalizza e basta.
        if (!Path.IsPathRooted(path)) return NormalizeSeparators(path);

        // Preferito: taglia dopo la cartella LAYOUT (parte comune a tutti gli utenti).
        var afterLayout = TailAfterLayout(path);
        if (afterLayout != null) return afterLayout;

        // Alternativa: taglia dopo la cartella del sito SharePoint.
        var afterSite = TailAfterSite(path);
        if (afterSite != null) return afterSite;

        // Non riconosciuto: lascia assoluto per non rompere casi particolari.
        return path;
    }

    // ──────────────────────────────────────────────────────────────
    //  LETTURA: relativo/legacy -> assoluto valido sul PC corrente
    // ──────────────────────────────────────────────────────────────
    public static string Resolve(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath)) return string.Empty;

        var path = Environment.ExpandEnvironmentVariables(storedPath.Trim());

        // 1) Path assoluto che esiste gia' cosi' com'e'.
        if (Path.IsPathRooted(path) && (File.Exists(path) || Directory.Exists(path)))
            return path;

        // 2) Costruisci tutte le "code relative" plausibili.
        var tails = BuildTailCandidates(path);

        // 3) Prova a combinarle con le root LAYOUT/libreria trovate sul PC.
        foreach (var root in GetLayoutRootCandidates())
        {
            foreach (var tail in tails)
            {
                if (string.IsNullOrWhiteSpace(tail)) continue;
                try
                {
                    var candidate = Path.GetFullPath(Path.Combine(root, tail));
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                        return candidate;
                }
                catch { /* combinazione non valida: ignora */ }
            }
        }

        // 4) Fallback: se era relativo, prova comunque a incollarlo sulla prima root nota.
        if (!Path.IsPathRooted(path))
        {
            var firstRoot = GetLayoutRootCandidates().FirstOrDefault();
            if (firstRoot != null)
            {
                try { return Path.GetFullPath(Path.Combine(firstRoot, NormalizeSeparators(path))); }
                catch { /* ignora */ }
            }
        }

        // 5) Ultima spiaggia: restituisci il valore originale (assoluto).
        return path;
    }

    public static bool Exists(string? storedPath)
    {
        var resolved = Resolve(storedPath);
        return File.Exists(resolved) || Directory.Exists(resolved);
    }

    // ──────────────────────────────────────────────────────────────
    //  ESTRAZIONE CODE RELATIVE
    // ──────────────────────────────────────────────────────────────
    private static List<string> BuildTailCandidates(string path)
    {
        var list = new List<string>();

        void AddIfAny(string? t) { if (!string.IsNullOrWhiteSpace(t)) list.Add(NormalizeSeparators(t!)); }

        // Se e' gia' relativo, e' esso stesso una coda valida.
        if (!Path.IsPathRooted(path)) AddIfAny(path);

        AddIfAny(TailAfterLayout(path));   // dopo ...\LAYOUT\  oppure  ...- LAYOUT\
        AddIfAny(TailAfterSite(path));      // dopo la cartella del sito

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Ritorna tutto cio' che segue l'ultimo segmento LAYOUT (o "... - LAYOUT").</summary>
    private static string? TailAfterLayout(string path)
    {
        var parts = SplitSegments(path);
        int idx = -1;
        for (int i = 0; i < parts.Length; i++)
        {
            var seg = parts[i];
            if (seg.Equals("LAYOUT", OIC) || seg.EndsWith("- LAYOUT", OIC))
                idx = i;
        }
        if (idx < 0 || idx >= parts.Length - 1) return null;
        return Path.Combine(parts.Skip(idx + 1).ToArray());
    }

    /// <summary>Ritorna tutto cio' che segue la cartella del sito SharePoint.</summary>
    private static string? TailAfterSite(string path)
    {
        var parts = SplitSegments(path);
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].StartsWith(SiteMarker, OIC))
                return Path.Combine(parts.Skip(i + 1).ToArray());
        }
        return null;
    }

    // ──────────────────────────────────────────────────────────────
    //  ROOT CANDIDATE SUL PC CORRENTE (cartelle che contengono i DWG)
    // ──────────────────────────────────────────────────────────────
    private static IEnumerable<string> GetLayoutRootCandidates()
    {
        var roots = new List<string>();

        // Override manuali.
        AddValidDir(roots, Environment.GetEnvironmentVariable(LayoutRootEnvVar));
        AddValidDir(roots, Environment.GetEnvironmentVariable(LibraryRootEnvVar));

        // Cartelle del sito "Industrial Electrical Design Engineering..." sincronizzate localmente.
        foreach (var siteDir in FindSiteDirectories())
        {
            // Il sito stesso puo' essere gia' la libreria (es. "... - LAYOUT").
            AddValidDir(roots, siteDir);

            // Oppure la LAYOUT e' una sottocartella (es. "... - Documenti\LAYOUT").
            AddValidDir(roots, Path.Combine(siteDir, "LAYOUT"));
            AddValidDir(roots, Path.Combine(siteDir, "Documenti", "LAYOUT"));
        }

        // Fallback generici.
        AddValidDir(roots, AppContext.BaseDirectory);
        AddValidDir(roots, Directory.GetCurrentDirectory());

        return roots.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Cerca in modo mirato le cartelle del sito SharePoint sotto il profilo utente.</summary>
    private static IEnumerable<string> FindSiteDirectories()
    {
        var found = new List<string>();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile) || !Directory.Exists(userProfile))
            return found;

        // Basi tipiche dove OneDrive/SharePoint sincronizza (es. GruppoAB, OneDrive - GruppoAB, ...).
        var bases = new List<string> { userProfile };
        foreach (var d in EnumerateDirs(userProfile))
            bases.Add(d);

        foreach (var b in bases.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var d in EnumerateDirs(b))
            {
                var name = Path.GetFileName(d);
                if (name.StartsWith(SiteMarker, OIC) || name.IndexOf(SiteMarker, OIC) >= 0)
                    found.Add(d);
            }
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // ──────────────────────────────────────────────────────────────
    //  HELPER
    // ──────────────────────────────────────────────────────────────
    private static string[] SplitSegments(string path)
        => path.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                      StringSplitOptions.RemoveEmptyEntries);

    private static void AddValidDir(List<string> roots, string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        try
        {
            var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dir.Trim()));
            if (Directory.Exists(full)) roots.Add(full);
        }
        catch { /* path non valido */ }
    }

    private static IEnumerable<string> EnumerateDirs(string dir)
    {
        try { return Directory.GetDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }

    private static string NormalizeSeparators(string path)
        => path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim();
}
