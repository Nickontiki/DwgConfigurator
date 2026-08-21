using System.IO;
using DwgConfigurator.Shared.Data;
using DwgConfigurator.Shared.DwgEngine;

namespace DwgConfigurator.ConfiguratorApp.Services;

/// <summary>
/// Genera UN SINGOLO file DWG: merge cartiglio nel layout + scrittura attributi.
/// Il cartiglio è letto dal ProductGroup, il layout dal DwgTemplates.
/// </summary>
public class DwgGenerationService
{
    private readonly DwgTemplateRepository _templateRepo = new();
    private readonly ProductGroupRepository _groupRepo = new();

    /// <summary>
    /// Genera il DWG finale (singolo file).
    /// </summary>
    public string Generate(int productTypeId, string outputPath,
        Dictionary<string, string> resolvedAttributes)
    {
        // 1. Carica Layout dal DB (per prodotto)
        var layout = _templateRepo.GetLayout(productTypeId);
        if (layout == null)
            throw new InvalidOperationException(
                "Nessun template Layout configurato.\n\n" +
                "Vai in AdminApp -> Tipi Prodotto e associa un Layout.");

        // 2. Carica Cartiglio dal Gruppo
        var group = _groupRepo.GetByProductTypeId(productTypeId);
        if (group == null || string.IsNullOrWhiteSpace(group.CartiglioPath))
            throw new InvalidOperationException(
                "Nessun Cartiglio DWG configurato per il gruppo.\n\n" +
                "Vai in AdminApp -> Tipi Prodotto e assegna un Cartiglio al gruppo.");

        // 3. Verifica file
        if (!File.Exists(layout.TemplatePath))
            throw new FileNotFoundException(
                $"Template Layout non trovato:\n\n{layout.TemplatePath}");

        if (!File.Exists(group.CartiglioPath))
            throw new FileNotFoundException(
                $"Template Cartiglio non trovato:\n\n{group.CartiglioPath}");

        // 4. Genera: merge + scrittura attributi + salva
        var cartiglioFormat = NormalizeFormat(layout.Format);
        return DwgWriter.GenerateOutput(
            layout.TemplatePath,
            group.CartiglioPath,
            outputPath,
            resolvedAttributes,
            cartiglioFormat);
    }
    private static string NormalizeFormat(string? format)
    {
        var value = (format ?? string.Empty).Trim().ToUpperInvariant();
        return value == "A0" ? "A0" : "A1";
    }
}
