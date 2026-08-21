namespace DwgConfigurator.Shared.Models;

/// <summary>
/// Modulo associabile a un gruppo prodotto (es. "Modulo motore" → "MM").
/// </summary>
public class ModuleInfo
{
    public int Id { get; set; }

    /// <summary>Nome completo, es. "Modulo motore"</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Sigla breve, es. "MM"</summary>
    public string Sigla { get; set; } = string.Empty;

    /// <summary>Testo visualizzato: "Modulo motore (MM)"</summary>
    public string DisplayText => $"{Name} ({Sigla})";

    public override string ToString() => DisplayText;
}
