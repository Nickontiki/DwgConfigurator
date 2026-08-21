using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using DwgConfigurator.Shared.Data;
using DwgConfigurator.Shared.DwgEngine;
using DwgConfigurator.Shared.Models;
using DwgConfigurator.ConfiguratorApp.Services;

namespace DwgConfigurator.ConfiguratorApp.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ProductGroupRepository _groupRepo = new();
    private readonly ProductTypeRepository _productTypeRepo = new();
    private readonly FixedAttributeRepository _fixedAttrRepo = new();
    private readonly DwgTemplateRepository _templateRepo = new();
    private readonly ModuleRepository _moduleRepo = new();
    private readonly DwgGenerationService _generationService = new();
    private readonly CommessaRepository _commessaRepo = new();
    private readonly UserRepository _userRepo = new();

    // Cache dei ProductType della gamma corrente (per filtri Modulo/Famiglia/Carpenteria)
    private List<ProductType> _typesInGroup = new();

    // Timer per debounce ricerca commessa
    private readonly DispatcherTimer _searchTimer;
    private bool _isFillingFromSuggestion;
    private bool _isFillingCarpenteria;

    public MainViewModel()
    {
        GenerateCommand = new RelayCommand(OnGenerate, CanGenerate);
        ShowCarpenterieCommand = new RelayCommand(_ => ShowAllCarpenterieSuggestions(), _ => IsCarpenteriaEnabled);

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = SearchCommesseAsync();
        };

        // Valori di default
        _drawingTitle1 = "Internal electrical setup";
        _drawingTitle2 = "Allestimento elettrico interno";

        // Valori di default per il blocco revisione
        _revRevision = "0";
        _revDate = DateTime.Now.ToString("dd/MM/yyyy");
        _revDescription = "First issue / Prima emissione";
        _revDrawn = FormatWindowsUser();
        LoadRevisionUserInfo();

        LoadProductGroups();
    }

    // ═══════════════════════════════════════════════════════════
    //  COMMESSA + AUTOCOMPLETE
    // ═══════════════════════════════════════════════════════════
    private string _commessa = string.Empty;
    public string Commessa
    {
        get => _commessa;
        set
        {
            _commessa = value;
            OnPropertyChanged();
            UpdateComputedFields();
            RefreshPreview();

            // Avvia ricerca con debounce (solo se NON stiamo compilando da suggerimento)
            if (!_isFillingFromSuggestion && value?.Length >= 2)
            {
                _searchTimer.Stop();
                _searchTimer.Start();
            }
            else if (value?.Length < 2)
            {
                CommessaSuggestions.Clear();
                IsCommessaPopupOpen = false;
            }
        }
    }

    public ObservableCollection<CommessaInfo> CommessaSuggestions { get; } = new();

    private bool _isCommessaPopupOpen;
    public bool IsCommessaPopupOpen
    {
        get => _isCommessaPopupOpen;
        set { _isCommessaPopupOpen = value; OnPropertyChanged(); }
    }

    private CommessaInfo? _selectedCommessaSuggestion;
    public CommessaInfo? SelectedCommessaSuggestion
    {
        get => _selectedCommessaSuggestion;
        set
        {
            _selectedCommessaSuggestion = value;
            OnPropertyChanged();
            if (value != null)
                ApplyCommessaData(value);
        }
    }

    private async Task SearchCommesseAsync()
    {
        var searchText = Commessa;
        try
        {
            var results = await Task.Run(() => _commessaRepo.SearchCommesse(searchText));

            // Verifica che l'utente non abbia cambiato testo nel frattempo
            if (Commessa != searchText) return;

            CommessaSuggestions.Clear();
            foreach (var r in results)
                CommessaSuggestions.Add(r);

            IsCommessaPopupOpen = CommessaSuggestions.Count > 0;
        }
        catch
        {
            CommessaSuggestions.Clear();
            IsCommessaPopupOpen = false;
        }
    }

    private void ApplyCommessaData(CommessaInfo suggestion)
    {
        _isFillingFromSuggestion = true;
        IsCommessaPopupOpen = false;

        // Recupera dati completi dal DB
        var full = _commessaRepo.GetCommessa(suggestion.OrderCode);
        if (full == null)
        {
            _isFillingFromSuggestion = false;
            return;
        }

        // Compila i campi
        Commessa = full.OrderCode;
        Customer1 = full.Company;
        FinalClient1 = full.Post1;

        // Sito di installazione
        var streetCity = full.Street;
        if (!string.IsNullOrWhiteSpace(full.Ort01))
            streetCity += (string.IsNullOrWhiteSpace(streetCity) ? "" : ", ") + full.Ort01;
        InstallationSite1 = streetCity;
        InstallationSite2 = full.Pstlz;
        InstallationSite3 = full.Regio;
        InstallationSite4 = full.Land1;

        // OrderText = commessa
        OrderText = full.OrderCode;

        _isFillingFromSuggestion = false;
        RefreshPreview();
    }

    // ═══════════════════════════════════════════════════════════
    //  CAMPI CALCOLATI AUTOMATICAMENTE
    // ═══════════════════════════════════════════════════════════
    private void UpdateComputedFields()
    {
        if (!string.IsNullOrWhiteSpace(Commessa) && SelectedModule != null)
        {
            var sigla = GetModuleSigla();
            DrawingNumber = $"{Commessa}_{sigla}_REV0";
        }
        else
            DrawingNumber = string.Empty;
    }

    private void UpdateFieldsFromDrawingNumber()
    {
        if (!string.IsNullOrWhiteSpace(DrawingNumber))
        {
            BarCode = $"*{DrawingNumber}*";
            FileText = $"{DrawingNumber}.dwg";
        }
        else
        {
            BarCode = string.Empty;
            FileText = string.Empty;
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  DEBUG
    // ═══════════════════════════════════════════════════════════
    private bool _isDebugEnabled;
    /// <summary>Se attivo, salva il log diagnostico e offre di aprirlo.</summary>
    public bool IsDebugEnabled
    {
        get => _isDebugEnabled;
        set { _isDebugEnabled = value; OnPropertyChanged(); }
    }

    // ═══════════════════════════════════════════════════════════
    //  SELEZIONE: GAMMA / MODULO / FAMIGLIA / CARPENTERIA
    // ═══════════════════════════════════════════════════════════
    public ObservableCollection<ProductGroup> ProductGroups { get; } = new();
    public ObservableCollection<ModuleInfo> Modules { get; } = new();
    public ObservableCollection<string> Famiglie { get; } = new();

    // Elenco completo carpenterie disponibili per Gamma+Modulo+Famiglia
    private List<string> _carpenterieMaster = new();
    // Suggerimenti filtrati mostrati nel popup
    public ObservableCollection<string> CarpenterieSuggestions { get; } = new();

    private ProductGroup? _selectedProductGroup;
    public ProductGroup? SelectedProductGroup
    {
        get => _selectedProductGroup;
        set { _selectedProductGroup = value; OnPropertyChanged(); LoadModules(); }
    }

    private ModuleInfo? _selectedModule;
    public ModuleInfo? SelectedModule
    {
        get => _selectedModule;
        set
        {
            _selectedModule = value;
            OnPropertyChanged();
            LoadFamiglie();
            UpdateComputedFields();
        }
    }

    private string? _selectedFamiglia;
    public string? SelectedFamiglia
    {
        get => _selectedFamiglia;
        set
        {
            _selectedFamiglia = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsCarpenteriaEnabled));
            LoadCarpenterie();
        }
    }

    /// <summary>La carpenteria e' compilabile solo se e' stata selezionata una famiglia.</summary>
    public bool IsCarpenteriaEnabled => SelectedFamiglia != null;

    private string _carpenteria = string.Empty;
    public string Carpenteria
    {
        get => _carpenteria;
        set
        {
            _carpenteria = value;
            OnPropertyChanged();

            if (!_isFillingCarpenteria && IsCarpenteriaEnabled)
                FilterCarpenterieSuggestions();

            ResolveProductType();
        }
    }

    private bool _isCarpenteriaPopupOpen;
    public bool IsCarpenteriaPopupOpen
    {
        get => _isCarpenteriaPopupOpen;
        set { _isCarpenteriaPopupOpen = value; OnPropertyChanged(); }
    }

    private string? _selectedCarpenteriaSuggestion;
    public string? SelectedCarpenteriaSuggestion
    {
        get => _selectedCarpenteriaSuggestion;
        set
        {
            _selectedCarpenteriaSuggestion = value;
            OnPropertyChanged();
            if (value != null)
            {
                _isFillingCarpenteria = true;
                Carpenteria = value;
                IsCarpenteriaPopupOpen = false;
                _isFillingCarpenteria = false;
            }
        }
    }

    // ProductType risolto (interno): guida la selezione del layout
    private ProductType? _selectedProductType;
    public ProductType? SelectedProductType
    {
        get => _selectedProductType;
        set { _selectedProductType = value; OnPropertyChanged(); OnProductTypeChanged(); }
    }

    private void LoadModules()
    {
        Modules.Clear();
        Famiglie.Clear();
        _carpenterieMaster = new();
        CarpenterieSuggestions.Clear();
        _selectedFamiglia = null; OnPropertyChanged(nameof(SelectedFamiglia));
        OnPropertyChanged(nameof(IsCarpenteriaEnabled));

        _typesInGroup = SelectedProductGroup == null
            ? new List<ProductType>()
            : _productTypeRepo.GetByGroupId(SelectedProductGroup.Id).ToList();

        if (SelectedProductGroup != null)
        {
            var moduleIds = _typesInGroup
                .Where(t => t.ModuleId.HasValue)
                .Select(t => t.ModuleId!.Value)
                .Distinct();

            foreach (var mid in moduleIds)
            {
                var m = _moduleRepo.GetById(mid);
                if (m != null) Modules.Add(m);
            }
        }

        SelectedModule = Modules.FirstOrDefault();
    }

    private void LoadFamiglie()
    {
        Famiglie.Clear();
        _carpenterieMaster = new();
        CarpenterieSuggestions.Clear();
        _selectedFamiglia = null; OnPropertyChanged(nameof(SelectedFamiglia));
        OnPropertyChanged(nameof(IsCarpenteriaEnabled));

        if (SelectedModule != null)
        {
            var famiglie = _typesInGroup
                .Where(t => t.ModuleId == SelectedModule.Id && !string.IsNullOrWhiteSpace(t.Famiglia))
                .Select(t => t.Famiglia.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f);

            foreach (var f in famiglie) Famiglie.Add(f);
        }

        SelectedFamiglia = Famiglie.FirstOrDefault();
    }

    private void LoadCarpenterie()
    {
        _carpenterieMaster = new();
        CarpenterieSuggestions.Clear();

        if (SelectedModule != null && !string.IsNullOrWhiteSpace(SelectedFamiglia))
        {
            _carpenterieMaster = _typesInGroup
                .Where(t => t.ModuleId == SelectedModule.Id &&
                            string.Equals(t.Famiglia ?? "", SelectedFamiglia, StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(t.Carpenteria))
                .Select(t => t.Carpenteria.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
        }

        // Pre-seleziona la prima carpenteria disponibile (comodo, ma resta editabile)
        _isFillingCarpenteria = true;
        Carpenteria = _carpenterieMaster.FirstOrDefault() ?? string.Empty;
        _isFillingCarpenteria = false;

        IsCarpenteriaPopupOpen = false;
        ResolveProductType();
    }

    private void FilterCarpenterieSuggestions()
    {
        CarpenterieSuggestions.Clear();

        var text = (Carpenteria ?? string.Empty).Trim();
        IEnumerable<string> matches = _carpenterieMaster;

        if (text.Length > 0)
            matches = _carpenterieMaster
                .Where(c => c.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);

        foreach (var c in matches) CarpenterieSuggestions.Add(c);

        IsCarpenteriaPopupOpen = CarpenterieSuggestions.Count > 0;
    }

    /// <summary>
    /// Mostra TUTTE le carpenterie disponibili per la famiglia selezionata,
    /// senza bisogno di scrivere. Usato dal pulsante freccia accanto al campo.
    /// </summary>
    private void ShowAllCarpenterieSuggestions()
    {
        if (!IsCarpenteriaEnabled) return;

        CarpenterieSuggestions.Clear();
        foreach (var c in _carpenterieMaster) CarpenterieSuggestions.Add(c);

        IsCarpenteriaPopupOpen = CarpenterieSuggestions.Count > 0;
    }

    /// <summary>
    /// Risolve il ProductType (quindi il layout) in base a Gamma+Modulo+Famiglia+Carpenteria.
    /// Se non c'e' corrispondenza esatta, SelectedProductType resta null.
    /// </summary>
    private void ResolveProductType()
    {
        ProductType? match = null;

        if (SelectedProductGroup != null && SelectedModule != null &&
            !string.IsNullOrWhiteSpace(SelectedFamiglia))
        {
            var carp = (Carpenteria ?? string.Empty).Trim();
            match = _typesInGroup.FirstOrDefault(t =>
                t.ModuleId == SelectedModule.Id &&
                string.Equals(t.Famiglia ?? "", SelectedFamiglia, StringComparison.OrdinalIgnoreCase) &&
                string.Equals((t.Carpenteria ?? string.Empty).Trim(), carp, StringComparison.OrdinalIgnoreCase));
        }

        SelectedProductType = match;
    }

    // ═══════════════════════════════════════════════════════════
    //  ATTRIBUTI DINAMICI (22 campi)
    // ═══════════════════════════════════════════════════════════
    private string _customer1 = string.Empty;
    public string Customer1 { get => _customer1; set { _customer1 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _customer2 = string.Empty;
    public string Customer2 { get => _customer2; set { _customer2 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _finalClient1 = string.Empty;
    public string FinalClient1 { get => _finalClient1; set { _finalClient1 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _finalClient2 = string.Empty;
    public string FinalClient2 { get => _finalClient2; set { _finalClient2 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _finalClient3 = string.Empty;
    public string FinalClient3 { get => _finalClient3; set { _finalClient3 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _installationSite1 = string.Empty;
    public string InstallationSite1 { get => _installationSite1; set { _installationSite1 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _installationSite2 = string.Empty;
    public string InstallationSite2 { get => _installationSite2; set { _installationSite2 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _installationSite3 = string.Empty;
    public string InstallationSite3 { get => _installationSite3; set { _installationSite3 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _installationSite4 = string.Empty;
    public string InstallationSite4 { get => _installationSite4; set { _installationSite4 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _orderText = string.Empty;
    public string OrderText { get => _orderText; set { _orderText = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _drawingTitle1 = string.Empty;
    public string DrawingTitle1 { get => _drawingTitle1; set { _drawingTitle1 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _drawingTitle2 = string.Empty;
    public string DrawingTitle2 { get => _drawingTitle2; set { _drawingTitle2 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _drawingTitle3 = string.Empty;
    public string DrawingTitle3 { get => _drawingTitle3; set { _drawingTitle3 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _drawingTitle4 = string.Empty;
    public string DrawingTitle4 { get => _drawingTitle4; set { _drawingTitle4 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _model1 = string.Empty;
    public string Model1 { get => _model1; set { _model1 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _model2 = string.Empty;
    public string Model2 { get => _model2; set { _model2 = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _scaleText = string.Empty;
    public string ScaleText { get => _scaleText; set { _scaleText = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _formatText = string.Empty;
    public string FormatText { get => _formatText; set { _formatText = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _fileText = string.Empty;
    public string FileText { get => _fileText; set { _fileText = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _drawingNumber = string.Empty;
    public string DrawingNumber
    {
        get => _drawingNumber;
        set
        {
            _drawingNumber = value;
            OnPropertyChanged();
            UpdateFieldsFromDrawingNumber();
            RefreshPreview();
        }
    }
    private string _barCode = string.Empty;
    public string BarCode { get => _barCode; set { _barCode = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _revRevision = string.Empty;
    public string RevRevision { get => _revRevision; set { _revRevision = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _revDate = string.Empty;
    public string RevDate { get => _revDate; set { _revDate = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _revDescription = string.Empty;
    public string RevDescription { get => _revDescription; set { _revDescription = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _revDrawn = string.Empty;
    public string RevDrawn { get => _revDrawn; set { _revDrawn = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _revChecked = string.Empty;
    public string RevChecked { get => _revChecked; set { _revChecked = value; OnPropertyChanged(); RefreshPreview(); } }
    private string _approved = string.Empty;
    public string Approved { get => _approved; set { _approved = value; OnPropertyChanged(); RefreshPreview(); } }

    // ═══════════════════════════════════════════════════════════
    //  ANTEPRIMA + FIXED ATTRIBUTES
    // ═══════════════════════════════════════════════════════════
    private Dictionary<string, string> _cartiglioFixed = new();
    private Dictionary<string, string> _layoutFixed = new();

    private Dictionary<string, string> _previewAttributes = new();
    public Dictionary<string, string> PreviewAttributes
    {
        get => _previewAttributes;
        set { _previewAttributes = value; OnPropertyChanged(); }
    }

    private List<KeyValuePair<string, string>> _cartiglioPreviewAttributes = new();
    public List<KeyValuePair<string, string>> CartiglioPreviewAttributes
    {
        get => _cartiglioPreviewAttributes;
        set { _cartiglioPreviewAttributes = value; OnPropertyChanged(); }
    }

    private List<KeyValuePair<string, string>> _legendaPreviewAttributes = new();
    public List<KeyValuePair<string, string>> LegendaPreviewAttributes
    {
        get => _legendaPreviewAttributes;
        set { _legendaPreviewAttributes = value; OnPropertyChanged(); }
    }

    private string _statusMessage = string.Empty;
    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public ICommand GenerateCommand { get; }
    public ICommand ShowCarpenterieCommand { get; }

    // ═══════════════════════════════════════════════════════════
    //  METODI
    // ═══════════════════════════════════════════════════════════
    private void LoadProductGroups()
    {
        ProductGroups.Clear();
        foreach (var g in _groupRepo.GetAll()) ProductGroups.Add(g);
        if (ProductGroups.Count > 0) SelectedProductGroup = ProductGroups[0];
    }

    private void OnProductTypeChanged()
    {
        if (SelectedProductType == null)
        {
            _cartiglioFixed = new(); _layoutFixed = new();
            FormatText = string.Empty;
        }
        else
        {
            _cartiglioFixed = _fixedAttrRepo.GetDictionaryByProductTypeId(SelectedProductType.Id, "Cartiglio");
            _layoutFixed = _fixedAttrRepo.GetDictionaryByProductTypeId(SelectedProductType.Id, "Layout");
            var layout = _templateRepo.GetLayout(SelectedProductType.Id);
            SelectedProductType.LayoutFormat = NormalizeFormat(layout?.Format);
            FormatText = SelectedProductType.LayoutFormat;
        }
        RefreshPreview();
    }

    private Dictionary<string, string> BuildUserInput()
    {
        return new Dictionary<string, string>
        {
            ["CUSTOMER1"] = Customer1, ["CUSTOMER2"] = Customer2,
            ["FINALCLIENT1"] = FinalClient1, ["FINALCLIENT2"] = FinalClient2, ["FINALCLIENT3"] = FinalClient3,
            ["INSTALLATIONSITE1"] = InstallationSite1, ["INSTALLATIONSITE2"] = InstallationSite2,
            ["INSTALLATIONSITE3"] = InstallationSite3, ["INSTALLATIONSITE4"] = InstallationSite4,
            ["ORDERTEXT"] = OrderText,
            ["DRAWINGTITLE1"] = DrawingTitle1, ["DRAWINGTITLE2"] = DrawingTitle2,
            ["DRAWINGTITLE3"] = DrawingTitle3, ["DRAWINGTITLE4"] = DrawingTitle4,
            ["MODEL1"] = Model1, ["MODEL2"] = Model2,
            ["SCALETEXT"] = ScaleText, ["FORMATTEXT"] = GetSelectedLayoutFormat(), ["FILETEXT"] = FileText,
            ["DRAWINGNUMBER"] = DrawingNumber, ["BAR_CODE"] = BarCode,
            ["REV_REVISION"] = RevRevision, ["REV_DATE"] = RevDate,
            ["REV_ISSUE"] = RevDescription, ["REV_DRAWN"] = RevDrawn, ["REV_CHECKED"] = RevChecked,
            ["APPROVED"] = Approved,
        };
    }

    private void RefreshPreview()
    {
        var userInput = BuildUserInput();
        PreviewAttributes = AttributeResolver.Resolve(userInput, _cartiglioFixed, _layoutFixed);

        var cartiglioTags = new HashSet<string>(AttributeResolver.DynamicTags, StringComparer.OrdinalIgnoreCase);
        foreach (var t in AttributeResolver.CartiglioFixedTags)
            cartiglioTags.Add(t);
        var layoutTags = new HashSet<string>(AttributeResolver.LayoutFixedTags, StringComparer.OrdinalIgnoreCase);

        CartiglioPreviewAttributes = PreviewAttributes
            .Where(kv => cartiglioTags.Contains(kv.Key))
            .OrderBy(kv => kv.Key)
            .ToList();

        LegendaPreviewAttributes = PreviewAttributes
            .Where(kv => layoutTags.Contains(kv.Key))
            .OrderBy(kv => kv.Key)
            .ToList();
    }

    private string GetSelectedLayoutFormat()
    {
        if (SelectedProductType == null) return NormalizeFormat(FormatText);
        return NormalizeFormat(SelectedProductType.LayoutFormat);
    }

    private static string NormalizeFormat(string? format)
    {
        var value = (format ?? string.Empty).Trim().ToUpperInvariant();
        return value == "A0" ? "A0" : "A1";
    }

    private bool CanGenerate(object? _) =>
        SelectedProductType != null && !string.IsNullOrWhiteSpace(Commessa);

    private void OnGenerate(object? _)
    {
        try
        {
            if (SelectedProductType == null || string.IsNullOrWhiteSpace(Commessa)) return;

            // Nome file = DrawingNumber (se disponibile), altrimenti fallback
            var fileName = !string.IsNullOrWhiteSpace(DrawingNumber)
                ? DrawingNumber
                : $"{string.Join("_", Commessa.Split(Path.GetInvalidFileNameChars()))}_{DateTime.Now:yyyyMMdd_HHmmss}";

            // Rimuovi caratteri non validi dal fileName
            fileName = string.Join("_", fileName.Split(Path.GetInvalidFileNameChars()));

            var dlg = new SaveFileDialog
            {
                Title = "Salva DWG generato",
                Filter = "File DWG|*.dwg",
                FileName = $"{fileName}.dwg",
                DefaultExt = ".dwg"
            };

            if (dlg.ShowDialog() != true)
            {
                StatusMessage = "Generazione annullata.";
                return;
            }

            StatusMessage = "Generazione in corso...";

            // Aggiorna FILETEXT con il nome file effettivo scelto dall'utente
            FileText = Path.GetFileName(dlg.FileName);

            var userInput = BuildUserInput();
            var resolved = AttributeResolver.Resolve(userInput, _cartiglioFixed, _layoutFixed);

            var diagnosticLog = _generationService.Generate(
                SelectedProductType.Id, dlg.FileName, resolved);

            // Log diagnostico solo se Debug e' attivo
            if (IsDebugEnabled)
            {
                var logPath = Path.ChangeExtension(dlg.FileName, ".log.txt");
                File.WriteAllText(logPath, diagnosticLog, System.Text.Encoding.UTF8);

                StatusMessage = $"DWG generato: {Path.GetFileName(dlg.FileName)}";
                var msg = MessageBox.Show(
                    $"DWG generato con successo!\n\n" +
                    $"File: {Path.GetFileName(dlg.FileName)}\n" +
                    $"Log:  {Path.GetFileName(logPath)}\n" +
                    $"Cartella: {Path.GetDirectoryName(dlg.FileName)}\n\n" +
                    "Si -> Apri cartella\n" +
                    "No -> Apri log diagnostico",
                    "Generazione Completata",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Information);

                if (msg == MessageBoxResult.Yes)
                    Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\"");
                else if (msg == MessageBoxResult.No)
                    Process.Start(new ProcessStartInfo(logPath) { UseShellExecute = true });
            }
            else
            {
                StatusMessage = $"DWG generato: {Path.GetFileName(dlg.FileName)}";
                var msg = MessageBox.Show(
                    $"DWG generato con successo!\n\n" +
                    $"File: {Path.GetFileName(dlg.FileName)}\n" +
                    $"Cartella: {Path.GetDirectoryName(dlg.FileName)}\n\n" +
                    "Aprire la cartella?",
                    "Generazione Completata",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (msg == MessageBoxResult.Yes)
                    Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\"");
            }
        }
        catch (FileNotFoundException ex)
        {
            StatusMessage = "File template non trovato";
            MessageBox.Show(ex.Message, "Template Mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = "Configurazione incompleta";
            MessageBox.Show(ex.Message, "Configurazione Mancante", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore: {ex.Message}";
            MessageBox.Show($"Errore:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>
    /// Formatta il nome utente Windows come "N.Cognome".
    /// Es: "nicola.festa" -> "N.Festa", "DOMAIN\nicola.festa" -> "N.Festa"
    /// </summary>
    private static string FormatWindowsUser()
    {
        try
        {
            var raw = Environment.UserName ?? "";
            // Rimuovi eventuale dominio
            if (raw.Contains("\\")) raw = raw.Substring(raw.LastIndexOf("\\") + 1);
            // Split su punto, spazio, underscore
            var parts = raw.Split(new[] { '.', ' ', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var first = parts[0];
                var last = parts[^1];
                return $"{char.ToUpper(first[0])}.{char.ToUpper(last[0])}{last.Substring(1).ToLower()}";
            }
            return raw;
        }
        catch { return Environment.UserName ?? ""; }
    }

    /// <summary>
    /// Carica check_user e approved_user da UserDB.db per l'utente corrente.
    /// </summary>
    private void LoadRevisionUserInfo()
    {
        try
        {
            var windowsUser = Environment.UserName ?? "";
            var (check, approved) = _userRepo.GetUserInfo(windowsUser);
            if (!string.IsNullOrWhiteSpace(check))
                _revChecked = check;
            if (!string.IsNullOrWhiteSpace(approved))
                _approved = approved;
        }
        catch { /* UserDB non raggiungibile: campi restano vuoti */ }
    }

    /// <summary>
    /// Ritorna la sigla del modulo selezionato (es. "MM", "MB"). Fallback "XX".
    /// </summary>
    private string GetModuleSigla()
    {
        var sigla = SelectedModule?.Sigla;
        return string.IsNullOrWhiteSpace(sigla) ? "XX" : sigla.Trim();
    }
}
