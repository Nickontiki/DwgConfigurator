namespace DwgConfigurator.Shared.Models;

/// <summary>
/// Dati recuperati dal database SAP per una commessa.
/// </summary>
public class CommessaInfo
{
    public string OrderCode { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Post1 { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string Ort01 { get; set; } = string.Empty;
    public string Pstlz { get; set; } = string.Empty;
    public string Regio { get; set; } = string.Empty;
    public string Land1 { get; set; } = string.Empty;

    /// <summary>Testo visualizzato nella tendina: "OrderCode - Cliente finale"</summary>
    public string DisplayText =>
        string.IsNullOrWhiteSpace(Post1)
            ? OrderCode
            : $"{OrderCode}  —  {Post1}";

    public override string ToString() => DisplayText;
}
