using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Services;

namespace RibbonXmlEditor.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const string DeployedRoot = @"C:\ACCORevit\";

    private readonly AppSettings _settings = AppSettings.Current;
    private readonly DispatcherTimer _revalidateTimer;
    private readonly List<Issue> _loadIssues = new();

    private DocumentViewModel? _document;
    private NodeViewModel? _selectedNode;
    private bool _noticeDismissed;

    public MainViewModel()
    {
        _revalidateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _revalidateTimer.Tick += (_, _) => { _revalidateTimer.Stop(); Revalidate(); };

        NewCommand = new RelayCommand(NewBlank);
        NewFromTemplateCommand = new RelayCommand(NewFromTemplate);
        OpenCommand = new RelayCommand(Open);
        OpenRecentCommand = new RelayCommand<string>(OpenFile);
        SaveCommand = new RelayCommand(() => Save(), () => Document is not null);
        SaveAsCommand = new RelayCommand(() => SaveAs(), () => Document is not null);
        OpenBackupsFolderCommand = new RelayCommand(OpenBackupsFolder);
        ExitCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));
        SelectDllCommand = new RelayCommand(SelectDll, () => Document is not null);
        RescanDllCommand = new RelayCommand(RescanDll, () => Document?.DllPath is not null);
        SetImagesFolderCommand = new RelayCommand(SetImagesFolder);
        AboutCommand = new RelayCommand(About);
        DismissNoticeCommand = new RelayCommand(() => { _noticeDismissed = true; OnPropertyChanged(nameof(ShowDeployedNotice)); });

        RecentFiles = new ObservableCollection<string>(_settings.RecentFiles);
        NewBlank(confirm: false);
    }

    // ---- state -----------------------------------------------------------------------

    public DocumentViewModel? Document
    {
        get => _document;
        private set
        {
            if (_document is not null)
            {
                _document.Changed -= OnDocumentChanged;
                _document.NodeSelected -= OnNodeSelected;
                _document.PropertyChanged -= OnDocumentPropertyChanged;
            }
            _document = value;
            if (_document is not null)
            {
                _document.Changed += OnDocumentChanged;
                _document.NodeSelected += OnNodeSelected;
                _document.PropertyChanged += OnDocumentPropertyChanged;
            }
            OnPropertyChanged();
            NotifyHeaderProperties();
            SaveCommand.RaiseCanExecuteChanged();
            SaveAsCommand.RaiseCanExecuteChanged();
            SelectDllCommand.RaiseCanExecuteChanged();
            RescanDllCommand.RaiseCanExecuteChanged();
        }
    }

    public NodeViewModel? SelectedNode
    {
        get => _selectedNode;
        private set => SetProperty(ref _selectedNode, value);
    }

    public ObservableCollection<string> RecentFiles { get; }
    public ObservableCollection<IssueViewModel> Issues { get; } = new();

    public int ErrorCount => Issues.Count(i => i.IsError);
    public int WarningCount => Issues.Count(i => !i.IsError);
    public string IssueSummary => Issues.Count == 0 ? "No issues" : $"{ErrorCount} error(s), {WarningCount} warning(s)";

    public string Title => Document is null
        ? "Ribbon XML Editor"
        : $"{Document.DisplayName}{(Document.IsDirty ? " *" : string.Empty)} - Ribbon XML Editor";

    public string StatusPath => Document?.FilePath ?? "Not saved yet";
    public bool IsDeployedCopy => Document?.FilePath?.StartsWith(DeployedRoot, StringComparison.OrdinalIgnoreCase) == true;
    public bool ShowDeployedNotice => IsDeployedCopy && !_noticeDismissed;
    public string ImagesFolder => _settings.EffectiveImagesFolder;
    public string DllStatusText => Document?.DllStatusText ?? string.Empty;

    // ---- commands --------------------------------------------------------------------

    public RelayCommand NewCommand { get; }
    public RelayCommand NewFromTemplateCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand<string> OpenRecentCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand OpenBackupsFolderCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand SelectDllCommand { get; }
    public RelayCommand RescanDllCommand { get; }
    public RelayCommand SetImagesFolderCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand DismissNoticeCommand { get; }

    /// <summary>Asks the window to close (after the usual save prompt).</summary>
    public event EventHandler? RequestClose;

    /// <summary>Asks the window to focus the editor for a field once the property panel has rendered.</summary>
    public event EventHandler<Fields.FieldViewModel>? FocusFieldRequested;

    // ---- file operations -------------------------------------------------------------

    private void NewBlank() => NewBlank(confirm: true);

    private void NewBlank(bool confirm)
    {
        if (confirm && !ConfirmDiscardChanges())
            return;
        LoadDocument(RibbonDocument.CreateBlank(), Array.Empty<Issue>(), needsRepair: false);
    }

    private void NewFromTemplate()
    {
        if (!ConfirmDiscardChanges())
            return;
        try
        {
            var result = TemplateProvider.LoadButtonStructureTemplate();
            result.Document.VersionDate = DateOnly.FromDateTime(DateTime.Today);
            LoadDocument(result.Document, result.Issues, result.NeedsRepairSave);
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Could not load the built-in template.\n\n{ex.Message}");
        }
    }

    private void Open()
    {
        var initial = Document?.FilePath is { } p ? Path.GetDirectoryName(p) : _settings.LastOpenFolder;
        var path = Dialogs.OpenRibbon(initial);
        if (path is not null)
            OpenFile(path);
    }

    public void OpenFile(string path)
    {
        if (!ConfirmDiscardChanges())
            return;
        try
        {
            var result = RibbonXmlReader.Load(path);
            LoadDocument(result.Document, result.Issues, result.NeedsRepairSave);
            RememberFile(path);
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Could not open\n{path}\n\n{ex.Message}");
            _settings.RecentFiles.Remove(path);
            _settings.Save();
            RefreshRecent();
        }
    }

    private void LoadDocument(RibbonDocument model, IEnumerable<Issue> loadIssues, bool needsRepair)
    {
        _noticeDismissed = false;
        _loadIssues.Clear();
        _loadIssues.AddRange(loadIssues);

        Document = new DocumentViewModel(model, this);
        if (needsRepair)
            Document.IsDirty = true;

        Document.Root.IsSelected = true;
        SelectedNode = Document.Root;

        AutoDetectDll();
        Revalidate();
    }

    public bool Save()
    {
        if (Document is null)
            return false;
        return Document.FilePath is null ? SaveAs() : SaveTo(Document.FilePath);
    }

    public bool SaveAs()
    {
        if (Document is null)
            return false;
        var initial = Document.FilePath is { } p ? Path.GetDirectoryName(p) : _settings.LastOpenFolder;
        var path = Dialogs.SaveRibbon(initial, Document.DisplayName);
        return path is not null && SaveTo(path);
    }

    private bool SaveTo(string path)
    {
        if (Document is null)
            return false;

        Revalidate();
        int errors = ErrorCount;
        if (errors > 0 && !Dialogs.Confirm(
                $"This file has {errors} error(s). Revit will most likely fail to load the tab, or load it incompletely.\n\nSave anyway?"))
            return false;

        foreach (var issue in RibbonValidator.CheckSaveTarget(path))
        {
            if (!Dialogs.Confirm(issue.Message + "\n\nSave anyway?"))
                return false;
        }

        try
        {
            BackupService.BackupIfExists(path);
            RibbonXmlWriter.Save(Document.Model, path);
            Document.IsDirty = false;
            Document.NotifyPathChanged();
            _loadIssues.Clear(); // load-time repairs are now written
            RememberFile(path);
            NotifyHeaderProperties();
            Revalidate();
            return true;
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Could not save\n{path}\n\n{ex.Message}");
            return false;
        }
    }

    /// <summary>Returns false when the user cancels. Offers to save unsaved changes first.</summary>
    public bool ConfirmDiscardChanges()
    {
        if (Document?.IsDirty != true)
            return true;
        return Dialogs.YesNoCancel($"Save changes to {Document.DisplayName}?") switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    public bool CanClose() => ConfirmDiscardChanges();

    private void RememberFile(string path)
    {
        _settings.AddRecent(path);
        _settings.LastOpenFolder = Path.GetDirectoryName(path);
        _settings.Save();
        RefreshRecent();
    }

    private void RefreshRecent()
    {
        RecentFiles.Clear();
        foreach (var f in _settings.RecentFiles)
            RecentFiles.Add(f);
    }

    private void OpenBackupsFolder()
    {
        try
        {
            Directory.CreateDirectory(BackupService.BackupRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupService.BackupRoot}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Error(ex.Message);
        }
    }

    private void SetImagesFolder()
    {
        var picked = Dialogs.PickFolder(Directory.Exists(ImagesFolder) ? ImagesFolder : null, "Select the ribbon Images folder");
        if (picked is null)
            return;
        _settings.ImagesFolder = picked;
        _settings.Save();
        OnPropertyChanged(nameof(ImagesFolder));
        Revalidate();
    }

    private void About()
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        Dialogs.Info(
            $"Ribbon XML Editor {version}\n\n" +
            "Edits the .ribbon files that define ACCO Revit ribbon tabs.\n\n" +
            "The file format is defined by RibbonBuilder.cs in the BTT_ACCORevit-Ribbons solution " +
            "(RevitRibbon_MainSourceCode\\Ribbon Builder). If that parser changes, update RibbonSchema.cs in this tool.\n\n" +
            $"Backups: {BackupService.BackupRoot}\nSettings: {AppSettings.SettingsPath}");
    }

    // ---- tab DLL / command classes ---------------------------------------------------

    private void SelectDll()
    {
        if (Document is null)
            return;
        var initial = Document.DllPath is { } d ? Path.GetDirectoryName(d)
                    : Document.FilePath is { } p ? Path.GetDirectoryName(p) : null;
        var picked = Dialogs.OpenDll(initial);
        if (picked is null)
            return;
        SetDll(picked, remember: true);
    }

    private void RescanDll()
    {
        if (Document?.DllPath is { } path)
            SetDll(path, remember: false);
    }

    private void AutoDetectDll()
    {
        if (Document?.FilePath is not { } ribbon)
            return;

        var candidates = new List<string>();
        var sameName = Path.ChangeExtension(ribbon, ".dll");
        if (File.Exists(sameName))
            candidates.Add(sameName);

        var dir = Path.GetDirectoryName(ribbon);
        if (dir is not null && Directory.Exists(dir))
        {
            var tabDlls = Directory.GetFiles(dir, "*_Tab.dll");
            if (tabDlls.Length == 1)
                candidates.Add(tabDlls[0]);
        }

        if (_settings.DllByRibbon.TryGetValue(ribbon, out var remembered) && File.Exists(remembered))
            candidates.Add(remembered);

        var pick = candidates.FirstOrDefault();
        if (pick is not null)
            SetDll(pick, remember: false);
    }

    private void SetDll(string dllPath, bool remember)
    {
        if (Document is null)
            return;

        Document.DllPath = dllPath;
        Document.ScannedClasses.Clear();
        var result = CommandClassScanner.Scan(dllPath);
        foreach (var c in result.ClassNames)
            Document.ScannedClasses.Add(c);
        Document.NotifyClassesChanged();
        OnPropertyChanged(nameof(DllStatusText));
        RescanDllCommand.RaiseCanExecuteChanged();

        if (result.Error is not null)
            Dialogs.Error($"Could not read command classes from\n{dllPath}\n\n{result.Error}");

        if (remember && Document.FilePath is { } ribbon)
        {
            _settings.DllByRibbon[ribbon] = dllPath;
            _settings.Save();
        }
        Revalidate();
    }

    // ---- validation ------------------------------------------------------------------

    private void OnDocumentChanged(object? sender, EventArgs e)
    {
        NotifyHeaderProperties();
        _revalidateTimer.Stop();
        _revalidateTimer.Start();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DocumentViewModel.IsDirty) or nameof(DocumentViewModel.DisplayName))
            NotifyHeaderProperties();
        if (e.PropertyName is nameof(DocumentViewModel.DllStatusText))
            OnPropertyChanged(nameof(DllStatusText));
    }

    private void OnNodeSelected(object? sender, NodeViewModel node) => SelectedNode = node;

    public void Revalidate()
    {
        _revalidateTimer.Stop();
        Issues.Clear();
        if (Document is null)
        {
            NotifyIssueCounts();
            return;
        }

        var context = new ValidationContext(
            KnownClasses: Document.ScannedClasses.Count > 0 ? new HashSet<string>(Document.ScannedClasses, StringComparer.Ordinal) : null,
            DllName: Document.DllPath is { } d ? Path.GetFileName(d) : null,
            ImagesFolder: ImagesFolder);

        var all = _loadIssues.Concat(RibbonValidator.Validate(Document.Model, context)).ToList();

        var byNode = all.Where(i => i.Node is not null).ToLookup(i => i.Node!);
        Document.Root.ApplyIssues(byNode);

        var vmByNode = Document.Root.DescendantsAndSelf().ToDictionary(v => v.Node);
        foreach (var issue in all.OrderByDescending(i => i.IsError))
        {
            NodeViewModel? node = issue.Node is not null && vmByNode.TryGetValue(issue.Node, out var v) ? v : null;
            Issues.Add(new IssueViewModel(issue, node));
        }
        NotifyIssueCounts();
    }

    public void NavigateTo(IssueViewModel issue)
    {
        if (issue.Node is null)
            return;
        issue.Node.ExpandAncestors();
        issue.Node.IsSelected = true;
        SelectedNode = issue.Node;
        if (issue.AttributeName is { } attr && issue.Node.GetField(attr) is { } field)
            FocusFieldRequested?.Invoke(this, field);
    }

    private void NotifyIssueCounts()
    {
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(IssueSummary));
    }

    private void NotifyHeaderProperties()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(StatusPath));
        OnPropertyChanged(nameof(IsDeployedCopy));
        OnPropertyChanged(nameof(ShowDeployedNotice));
        OnPropertyChanged(nameof(DllStatusText));
    }
}
