using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using DwgConfigurator.Shared.Data;
using DwgConfigurator.Shared.DwgEngine;
using DwgConfigurator.Shared.Models;

namespace DwgConfigurator.AdminApp.ViewModels;

public class FixedAttributeViewModel : INotifyPropertyChanged
{
    private readonly ProductGroupRepository _groupRepo = new();
    private readonly ProductTypeRepository _ptRepo = new();
    private readonly FixedAttributeRepository _fixedRepo = new();
    private readonly ModuleRepository _moduleRepo = new();
    private readonly DwgTemplateRepository _templateRepo = new();

    public class AttributeEntry : INotifyPropertyChanged
    {
        public string Tag { get; set; } = string.Empty;
        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public ObservableCollection<ProductGroup> ProductGroups { get; } = new();
    public ObservableCollection<ModuleInfo> Modules { get; } = new();
    public ObservableCollection<string> ModuleOptions { get; } = new();
    public ObservableCollection<AttributeEntry> Attributes { get; } = new();
    public List<string> AppliesToOptions { get; } = new() { "Cartiglio", "Layout" };

    private bool _isRefreshing;
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            _isRefreshing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RefreshButtonText));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string RefreshButtonText => IsRefreshing ? "Caricamento..." : "Aggiorna";

    private ProductGroup? _selectedProductGroup;
    public ProductGroup? SelectedProductGroup
    {
        get => _selectedProductGroup;
        set { _selectedProductGroup = value; OnPropertyChanged(); LoadAttributes(); }
    }

    private string? _selectedModuleOption;
    public string? SelectedModuleOption
    {
        get => _selectedModuleOption;
        set { _selectedModuleOption = value; OnPropertyChanged(); LoadAttributes(); }
    }

    private string _selectedAppliesTo = "Cartiglio";
    public string SelectedAppliesTo
    {
        get => _selectedAppliesTo;
        set { _selectedAppliesTo = value; OnPropertyChanged(); LoadAttributes(); }
    }

    public ICommand SaveCommand { get; }
    public ICommand RefreshCommand { get; }

    public FixedAttributeViewModel()
    {
        SaveCommand = new RelayCommand(_ => Save(), _ => SelectedProductGroup != null && GetSelectedModule() != null && !IsRefreshing);
        RefreshCommand = new RelayCommand(async _ => await RefreshAttributesFromTemplatesAsync(), _ => !IsRefreshing);
        RefreshProductTypes();
    }

    public void RefreshProductTypes()
    {
        var previousGroupId = SelectedProductGroup?.Id;
        var previousModule = SelectedModuleOption;

        ProductGroups.Clear();
        foreach (var g in _groupRepo.GetAll()) ProductGroups.Add(g);

        Modules.Clear();
        ModuleOptions.Clear();
        foreach (var m in _moduleRepo.GetAll())
        {
            Modules.Add(m);
            ModuleOptions.Add(m.DisplayText);
        }

        ProductGroup? selectedGroup = null;
        if (previousGroupId.HasValue) selectedGroup = ProductGroups.FirstOrDefault(g => g.Id == previousGroupId.Value);
        SelectedProductGroup = selectedGroup ?? ProductGroups.FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(previousModule) && ModuleOptions.Contains(previousModule)) SelectedModuleOption = previousModule;
        else SelectedModuleOption = ModuleOptions.FirstOrDefault();
    }

    private ModuleInfo? GetSelectedModule() => Modules.FirstOrDefault(m => m.DisplayText == SelectedModuleOption);

    private List<ProductType> GetCurrentProducts()
    {
        var group = SelectedProductGroup;
        var module = GetSelectedModule();
        if (group == null || module == null) return new List<ProductType>();
        return _ptRepo.GetByGroupAndModuleId(group.Id, module.Id).ToList();
    }

    private void LoadAttributes()
    {
        Attributes.Clear();
        var firstProduct = GetCurrentProducts().FirstOrDefault();
        var existing = firstProduct == null ? new Dictionary<string, string>() : _fixedRepo.GetDictionaryByProductTypeId(firstProduct.Id, SelectedAppliesTo);
        string[] tags = SelectedAppliesTo == "Cartiglio" ? AttributeResolver.CartiglioFixedTags : AttributeResolver.LayoutFixedTags;
        foreach (var tag in tags)
            Attributes.Add(new AttributeEntry { Tag = tag, Value = existing.TryGetValue(tag, out var val) ? val : string.Empty });
    }

    private async Task RefreshAttributesFromTemplatesAsync()
    {
        if (IsRefreshing) return;
        var group = SelectedProductGroup;
        var module = GetSelectedModule();
        if (group == null || module == null) return;

        IsRefreshing = true;
        try
        {
            await Task.Run(() => SyncAttributesFromTemplates(group.Id, module.Id));
            LoadAttributes();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore aggiornamento attributi:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void SyncAttributesFromTemplates(int groupId, int moduleId)
    {
        var group = _groupRepo.GetById(groupId);
        if (group != null && !string.IsNullOrWhiteSpace(group.CartiglioPath) && File.Exists(group.CartiglioPath))
        {
            var attrs = ExtractAllowed(group.CartiglioPath, AttributeResolver.CartiglioFixedTags);
            if (attrs.Count > 0)
                foreach (var product in _ptRepo.GetByGroupId(groupId))
                    _fixedRepo.MergeAttributes(product.Id, "Cartiglio", attrs);
        }

        foreach (var product in _ptRepo.GetByGroupAndModuleId(groupId, moduleId))
        {
            var layout = _templateRepo.GetLayout(product.Id);
            if (layout == null || string.IsNullOrWhiteSpace(layout.TemplatePath) || !File.Exists(layout.TemplatePath)) continue;
            var attrs = ExtractAllowed(layout.TemplatePath, AttributeResolver.LayoutFixedTags);
            if (attrs.Count > 0) _fixedRepo.MergeAttributes(product.Id, "Layout", attrs);
        }
    }

    private static Dictionary<string, string> ExtractAllowed(string path, string[] allowedTags)
    {
        var all = DwgReader.ExtractAttributes(path);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in all)
            if (allowedTags.Any(tag => tag.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))) result[kv.Key] = kv.Value;
        return result;
    }

    private void Save()
    {
        var group = SelectedProductGroup;
        var module = GetSelectedModule();
        if (group == null || module == null) return;
        var products = GetCurrentProducts();
        if (products.Count == 0)
        {
            MessageBox.Show("La combinazione Gamma + Modulo non contiene configurazioni.", "Nessuna configurazione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dict = new Dictionary<string, string>();
        foreach (var entry in Attributes)
            if (!string.IsNullOrWhiteSpace(entry.Tag)) dict[entry.Tag] = entry.Value ?? string.Empty;
        foreach (var product in products) _fixedRepo.BulkUpsert(product.Id, SelectedAppliesTo, dict);
        MessageBox.Show($"Attributi fissi ({SelectedAppliesTo}) salvati per Gamma \"{group.Name}\" / Modulo \"{module.DisplayText}\".\nConfigurazioni aggiornate: {products.Count}", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
