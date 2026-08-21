namespace DwgConfigurator.Shared.Models;

public class FixedAttribute
{
    public int Id { get; set; }
    public int ProductTypeId { get; set; }
    public string AttributeTag { get; set; } = string.Empty;
    public string FixedValue { get; set; } = string.Empty;
    public string AppliesTo { get; set; } = "Cartiglio";
}
