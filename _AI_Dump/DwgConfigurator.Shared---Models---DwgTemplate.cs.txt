namespace DwgConfigurator.Shared.Models;

/// <summary>
/// File DWG associato a un tipo di prodotto (Layout o Cartiglio).
/// </summary>
public class DwgTemplate
{
    public int Id { get; set; }
    public int ProductTypeId { get; set; }

    /// <summary>Percorso completo del file DWG template.</summary>
    public string TemplatePath { get; set; } = string.Empty;

    /// <summary>"Cartiglio" oppure "Layout".</summary>
    public string TemplateType { get; set; } = string.Empty;

    /// <summary>Formato foglio, es. A1, A3.</summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>Scala, es. 1:50.</summary>
    public string Scale { get; set; } = string.Empty;

    public bool IsCartiglio => TemplateType.Equals("Cartiglio", StringComparison.OrdinalIgnoreCase);
    public bool IsLayout    => TemplateType.Equals("Layout",    StringComparison.OrdinalIgnoreCase);
}
