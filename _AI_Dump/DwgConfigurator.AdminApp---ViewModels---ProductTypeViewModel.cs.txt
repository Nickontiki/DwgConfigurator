using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using DwgConfigurator.Shared.Data;
using DwgConfigurator.Shared.DwgEngine;
using DwgConfigurator.Shared.Models;
using DwgConfigurator.AdminApp.Views;

namespace DwgConfigurator.AdminApp.ViewModels;

public class ProductTypeViewModel : INotifyPropertyChanged
{
    private readonly ProductGroupRepository _groupRepo = new();
    private readonly ProductTypeRepository _productRepo = new();
    private readonly DwgTemplateRepository _templateRepo = new();
    private readonly FixedAttributeRepository _fixedRepo = new();
    private readonly ModuleRepository _moduleRepo = new();

    private const string NewModuleSentinel = "➕ Nuovo...";

    public ObservableCollection<ProductGroup> Groups { get; } = new();
    public ObservableCollection<ModuleInfo> Modules { get; } = new();
    public ObservableCollection<string> ModuleOptions { get; } = new();
    public ObservableCollection<string> TemperatureOptions { get; } = new() { "Standard", "-20°C" };
    public ObservableCollection<string> FormatOptions { get; } = new() { "A1", "A0" };
    public ObservableCollection<ProductType> Products { get; } = new();

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

    public string RefreshButtonText => IsRefreshing ? "Caricamento..." : "Carica attributi";

    private ProductGroup? _selectedGroup;
    public ProductGroup? SelectedGroup
    {
        get => _selectedGroup;
        set { _selectedGroup = value; OnPropertyChanged(); LoadProducts(); }
    }

    private string? _selectedModuleOption;
    public string? SelectedModuleOption
    {
        get => _selectedModuleOption;
        set
        {
            if (value == NewModuleSentinel)
            {
                OpenNewModuleDialog();
                return;
            }
            if (_selectedModuleOption == value) return;
            _selectedModuleOption = value;
            OnPropertyChanged();
            LoadProducts();
        }
    }

    private ProductType? _selectedProduct;
    public ProductType? SelectedProduct
    {
        get => _selectedProduct;
        set { _selectedProduct = value; OnPropertyChanged(); }
    }

    private string _newFamiglia = string.Empty;
    public string NewFamiglia { get => _newFamiglia; set { _newFamiglia = value; OnPropertyChanged(); } }

    private string _newCarpenteria = string.Empty;
    public string NewCarpenteria { get => _newCarpenteria; set { _newCarpenteria = value; OnPropertyChanged(); } }

    private string _newTemperatura = "Standard";
    public string NewTemperatura { get => _newTemperatura; set { _newTemperatura = value; OnPropertyChanged(); } }

    private string _newLayoutPath = string.Empty;
    public string NewLayoutPath { get => _newLayoutPath; set { _newLayoutPath = value; OnPropertyChanged(); } }
    private string _newLayoutFormat = "A1";
    public string NewLayoutFormat
    {
        get => string.IsNullOrWhiteSpace(_newLayoutFormat) ? "A1" : _newLayoutFormat;
        set { _newLayoutFormat = string.IsNullOrWhiteSpace(value) ? "A1" : value; OnPropertyChanged(); }
    }

    public ICommand NewGroupCommand { get; }
    public ICommand SaveGroupCommand { get; }
    public ICommand DeleteGroupCommand { get; }
    public ICommand DeleteModuleCommand { get; }
    public ICommand BrowseCartiglioCommand { get; }
    public ICommand AddProductCommand { get; }
    public ICommand DeleteProductCommand { get; }
    public ICommand SaveProductCommand { get; }
    public ICommand BrowseNewLayoutCommand { get; }
    public ICommand RefreshCommand { get; }

    public ProductTypeViewModel()
    {
        NewGroupCommand        = new RelayCommand(_ => OpenNewGroupDialog());
        SaveGroupCommand       = new RelayCommand(_ => SaveGroup(), _ => SelectedGroup != null && !IsRefreshing);
        DeleteGroupCommand     = new RelayCommand(_ => DeleteGroup(), _ => SelectedGroup != null && !IsRefreshing);
        DeleteModuleCommand    = new RelayCommand(_ => DeleteSelectedModule(), _ => GetSelectedModule() != null && !IsRefreshing);
        BrowseCartiglioCommand = new RelayCommand(_ => BrowseCartiglio(), _ => SelectedGroup != null && !IsRefreshing);
        AddProductCommand      = new RelayCommand(_ => AddProduct(), _ => SelectedGroup != null && GetSelectedModule() != null && !string.IsNullOrWhiteSpace(NewFamiglia) && !IsRefreshing);
        DeleteProductCommand   = new RelayCommand(_ => DeleteProduct(), _ => SelectedProduct != null && !IsRefreshing);
        SaveProductCommand     = new RelayCommand(_ => SaveProduct(), _ => SelectedProduct != null && !IsRefreshing);
        BrowseNewLayoutCommand = new RelayCommand(_ => BrowseNewLayout(), _ => !IsRefreshing);
        RefreshCommand         = new RelayCommand(async _ => await RefreshAllAsync(), _ => !IsRefreshing);

        RefreshAllUi();
    }

    private void RefreshAllUi()
    {
        var previousGroupId = SelectedGroup?.Id;
        var previousModule = SelectedModuleOption;

        LoadModuleOptions();
        LoadGroups(previousGroupId);

        if (!string.IsNullOrWhiteSpace(previousModule) && ModuleOptions.Contains(previousModule))
            _selectedModuleOption = previousModule;
        else if (_selectedModuleOption == null)
            _selectedModuleOption = ModuleOptions.FirstOrDefault(x => x != NewModuleSentinel);

        OnPropertyChanged(nameof(SelectedModuleOption));
        LoadProducts();
    }

    private async Task RefreshAllAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        try
        {
            var groupId = SelectedGroup?.Id;
            var moduleId = GetSelectedModule()?.Id;
            if (groupId.HasValue && moduleId.HasValue)
                await Task.Run(() => SyncAttributesFromTemplates(groupId.Value, moduleId.Value));
            RefreshAllUi();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore aggiornamento attributi:\n{ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { IsRefreshing = false; }
    }

    private void LoadModuleOptions()
    {
        Modules.Clear();
        ModuleOptions.Clear();
        foreach (var m in _moduleRepo.GetAll())
        {
            Modules.Add(m);
            ModuleOptions.Add(m.DisplayText);
        }
        ModuleOptions.Add(NewModuleSentinel);
    }

    private void LoadGroups(int? preferredGroupId = null)
    {
        Groups.Clear();
        foreach (var g in _groupRepo.GetAll()) Groups.Add(g);
        ProductGroup? selected = null;
        if (preferredGroupId.HasValue)
            selected = Groups.FirstOrDefault(g => g.Id == preferredGroupId.Value);
        SelectedGroup = selected ?? Groups.FirstOrDefault();
    }

    private ModuleInfo? GetSelectedModule() => Modules.FirstOrDefault(m => m.DisplayText == SelectedModuleOption);

    private void LoadProducts()
    {
        Products.Clear();
        SelectedProduct = null;
        var group = SelectedGroup;
        var module = GetSelectedModule();
        if (group == null || module == null) return;
        foreach (var p in _productRepo.GetByGroupAndModuleId(group.Id, module.Id))
        {
            var layout = _templateRepo.GetLayout(p.Id);
            p.LayoutPath = layout?.TemplatePath ?? string.Empty;
            p.LayoutFormat = NormalizeFormat(layout?.Format);
            Products.Add(p);
        }
    }

    private void SyncAttributesFromTemplates(int groupId, int moduleId)
    {
        var group = _groupRepo.GetById(groupId);
        if (group != null && !string.IsNullOrWhiteSpace(group.CartiglioPath) && File.Exists(group.CartiglioPath))
        {
            var cartiglioAttrs = ExtractAllowed(group.CartiglioPath, AttributeResolver.CartiglioFixedTags);
            if (cartiglioAttrs.Count > 0)
                foreach (var product in _productRepo.GetByGroupId(groupId))
                    _fixedRepo.MergeAttributes(product.Id, "Cartiglio", cartiglioAttrs);
        }

        foreach (var product in _productRepo.GetByGroupAndModuleId(groupId, moduleId))
        {
            var layout = _templateRepo.GetLayout(product.Id);
            if (layout == null || string.IsNullOrWhiteSpace(layout.TemplatePath) || !File.Exists(layout.TemplatePath)) continue;
            var layoutAttrs = ExtractAllowed(layout.TemplatePath, AttributeResolver.LayoutFixedTags);
            if (layoutAttrs.Count > 0)
                _fixedRepo.MergeAttributes(product.Id, "Layout", layoutAttrs);
        }
    }

    private static Dictionary<string, string> ExtractAllowed(string path, string[] allowedTags)
    {
        var all = DwgReader.ExtractAttributes(path);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in all)
            if (allowedTags.Any(tag => tag.Equals(kv.Key, StringComparison.OrdinalIgnoreCase)))
                result[kv.Key] = kv.Value;
        return result;
    }

    private void OpenNewModuleDialog()
    {
        var previous = _selectedModuleOption;
        var dlg = new NewModuleDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var mod = new ModuleInfo { Name = dlg.ModuleName, Sigla = dlg.ModuleSigla };
            mod.Id = _moduleRepo.Insert(mod);
            LoadModuleOptions();
            SelectedModuleOption = mod.DisplayText;
        }
        else
        {
            _selectedModuleOption = previous;
            OnPropertyChanged(nameof(SelectedModuleOption));
        }
    }

    private void DeleteSelectedModule()
    {
        var module = GetSelectedModule();
        if (module == null) return;
        if (_productRepo.ExistsForModule(module.Id))
        {
            MessageBox.Show("Impossibile eliminare il modulo perché è usato da una o più configurazioni.", "Modulo in uso", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var result = MessageBox.Show($"Eliminare il modulo \"{module.DisplayText}\"?", "Conferma eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _moduleRepo.Delete(module.Id);
        _selectedModuleOption = null;
        RefreshAllUi();
    }

    private void OpenNewGroupDialog()
    {
        var dlg = new NewGroupDialog { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() == true)
        {
            var g = new ProductGroup { Name = dlg.GroupName, Description = string.Empty, CartiglioPath = dlg.CartiglioPath };
            g.Id = _groupRepo.Insert(g);
            Groups.Add(g);
            SelectedGroup = g;
        }
    }

    private void SaveGroup()
    {
        var group = SelectedGroup;
        if (group == null) return;
        _groupRepo.Update(group);
        MessageBox.Show($"Gamma \"{group.Name}\" salvata.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteGroup()
    {
        var group = SelectedGroup;
        if (group == null) return;
        var result = MessageBox.Show($"Eliminare la Gamma \"{group.Name}\" e tutte le sue configurazioni?", "Conferma eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _groupRepo.Delete(group.Id);
        Groups.Remove(group);
        SelectedGroup = Groups.FirstOrDefault();
    }

    private void BrowseCartiglio()
    {
        var group = SelectedGroup;
        if (group == null) return;
        var dlg = new OpenFileDialog { Filter = "File DWG|*.dwg" };
        if (dlg.ShowDialog() == true)
        {
            group.CartiglioPath = dlg.FileName;
            _groupRepo.Update(group);
            MessageBox.Show($"Cartiglio aggiornato. Premi 'Carica attributi' per rileggere gli attributi.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void AddProduct()
    {
        var group = SelectedGroup;
        var module = GetSelectedModule();
        if (group == null || module == null) return;
        var temperatura = string.IsNullOrWhiteSpace(NewTemperatura) ? "Standard" : NewTemperatura.Trim();
        var p = new ProductType { ProductGroupId = group.Id, ModuleId = module.Id, Famiglia = NewFamiglia.Trim(), Carpenteria = NewCarpenteria.Trim(), Temperatura = temperatura, Prodotto = NewFamiglia.Trim(), Taglia = temperatura };
        p.Id = _productRepo.Insert(p);
        if (!string.IsNullOrWhiteSpace(NewLayoutPath))
        {
            var t = new DwgTemplate { ProductTypeId = p.Id, TemplatePath = NewLayoutPath.Trim(), TemplateType = "Layout", Format = NormalizeFormat(NewLayoutFormat) };
            t.Id = _templateRepo.Insert(t);
            p.LayoutPath = t.TemplatePath;
            p.LayoutFormat = t.Format;
        }
        else
        {
            p.LayoutFormat = NormalizeFormat(NewLayoutFormat);
        }
        Products.Add(p);
        SelectedProduct = p;
        NewFamiglia = string.Empty;
        NewCarpenteria = string.Empty;
        NewTemperatura = "Standard";
        NewLayoutPath = string.Empty;
        NewLayoutFormat = "A1";
    }

    private void DeleteProduct()
    {
        var product = SelectedProduct;
        if (product == null) return;
        var result = MessageBox.Show($"Eliminare la configurazione \"{product}\"?", "Conferma eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        _productRepo.Delete(product.Id);
        Products.Remove(product);
        SelectedProduct = Products.FirstOrDefault();
    }

    private void SaveProduct()
    {
        var product = SelectedProduct;
        if (product == null) return;
        product.Prodotto = product.Famiglia;
        product.Taglia = product.Temperatura;
        product.LayoutFormat = NormalizeFormat(product.LayoutFormat);
        _productRepo.Update(product);
        if (!string.IsNullOrWhiteSpace(product.LayoutPath))
        {
            var existing = _templateRepo.GetLayout(product.Id);
            if (existing != null)
            {
                existing.TemplatePath = product.LayoutPath;
                existing.Format = NormalizeFormat(product.LayoutFormat);
                _templateRepo.Update(existing);
            }
            else
            {
                var t = new DwgTemplate { ProductTypeId = product.Id, TemplatePath = product.LayoutPath, TemplateType = "Layout", Format = NormalizeFormat(product.LayoutFormat) };
                t.Id = _templateRepo.Insert(t);
            }
        }
        MessageBox.Show($"Configurazione \"{product}\" salvata. Premi 'Carica attributi' per rileggere gli attributi.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void UpdateProductLayout(ProductType product, string newLayoutPath)
    {
        product.LayoutPath = newLayoutPath;
        product.LayoutFormat = NormalizeFormat(product.LayoutFormat);
        var existing = _templateRepo.GetLayout(product.Id);
        if (existing != null)
        {
            existing.TemplatePath = newLayoutPath;
            existing.Format = NormalizeFormat(product.LayoutFormat);
            _templateRepo.Update(existing);
        }
        else
        {
            var t = new DwgTemplate { ProductTypeId = product.Id, TemplatePath = newLayoutPath, TemplateType = "Layout", Format = NormalizeFormat(product.LayoutFormat) };
            t.Id = _templateRepo.Insert(t);
        }
        MessageBox.Show($"Layout aggiornato per \"{product}\". Premi 'Carica attributi' per rileggere gli attributi.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BrowseNewLayout()
    {
        var dlg = new OpenFileDialog { Filter = "File DWG|*.dwg" };
        if (dlg.ShowDialog() == true) NewLayoutPath = dlg.FileName;
    }
    private static string NormalizeFormat(string? format)
    {
        var value = (format ?? string.Empty).Trim().ToUpperInvariant();
        return value == "A0" ? "A0" : "A1";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
