using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace DwgConfigurator.Shared.Models;

public class ProductType : INotifyPropertyChanged
{
    public int Id { get; set; }
    public int ProductGroupId { get; set; }
    public int? ModuleId { get; set; }

    private string _prodotto = string.Empty;
    public string Prodotto { get => _prodotto; set { _prodotto = value; OnPropertyChanged(); } }

    private string _taglia = string.Empty;
    public string Taglia { get => _taglia; set { _taglia = value; OnPropertyChanged(); } }

    private string _famiglia = string.Empty;
    public string Famiglia { get => _famiglia; set { _famiglia = value; OnPropertyChanged(); } }

    private string _carpenteria = string.Empty;
    public string Carpenteria { get => _carpenteria; set { _carpenteria = value; OnPropertyChanged(); } }

    private string _temperatura = "Standard";
    public string Temperatura
    {
        get => _temperatura;
        set { _temperatura = value; OnPropertyChanged(); OnPropertyChanged(nameof(TemperaturaDisplay)); }
    }

    public string TemperaturaDisplay => string.IsNullOrWhiteSpace(Temperatura) ? "Standard" : Temperatura;

    private string _layoutPath = string.Empty;
    public string LayoutPath
    {
        get => _layoutPath;
        set { _layoutPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(LayoutPathDisplay)); }
    }

    public string LayoutPathDisplay => string.IsNullOrEmpty(LayoutPath) ? "(nessun layout)" : Path.GetFileName(LayoutPath);

    private string _layoutFormat = "A1";
    /// <summary>Formato del layout/cartiglio associato alla configurazione: A1 oppure A0.</summary>
    public string LayoutFormat
    {
        get => string.IsNullOrWhiteSpace(_layoutFormat) ? "A1" : _layoutFormat;
        set { _layoutFormat = string.IsNullOrWhiteSpace(value) ? "A1" : value; OnPropertyChanged(); }
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Famiglia)) parts.Add(Famiglia);
        else if (!string.IsNullOrWhiteSpace(Prodotto)) parts.Add(Prodotto);
        if (!string.IsNullOrWhiteSpace(Carpenteria)) parts.Add(Carpenteria);
        if (!string.IsNullOrWhiteSpace(Temperatura)) parts.Add(Temperatura);
        return parts.Count > 0 ? string.Join(" | ", parts) : "(vuoto)";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
