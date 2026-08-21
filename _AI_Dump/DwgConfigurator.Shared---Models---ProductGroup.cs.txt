using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace DwgConfigurator.Shared.Models;

/// <summary>
/// Rappresenta un gruppo di prodotti (es. "Biogas", "Ecomax", "Motori").
/// Il CartiglioPath è unico per gruppo.
/// </summary>
public class ProductGroup : INotifyPropertyChanged
{
    public int Id { get; set; }

    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    private string _description = string.Empty;
    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    private string _cartiglioPath = string.Empty;
    /// <summary>Percorso completo del file DWG cartiglio per questo gruppo.</summary>
    public string CartiglioPath
    {
        get => _cartiglioPath;
        set { _cartiglioPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(CartiglioPathDisplay)); }
    }

    /// <summary>Solo il nome file per la visualizzazione nell'interfaccia.</summary>
    public string CartiglioPathDisplay =>
        string.IsNullOrEmpty(CartiglioPath) ? "(nessun cartiglio)" : Path.GetFileName(CartiglioPath);

    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
