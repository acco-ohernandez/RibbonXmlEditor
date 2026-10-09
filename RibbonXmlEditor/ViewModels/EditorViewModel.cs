using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Services;

namespace RibbonXmlEditor.ViewModels;

/// <summary>One open .ribbon file: its tree, selection, validation state, DLL context and file commands. One per tab.</summary>
public sealed class EditorViewModel : ObservableObject
{
    private const string DeployedRoot = @"C:\ACCORevit\";

    private readonly MainViewModel _shell;
    private readonly AppSettings _settings = AppSettings.Current;
    private readonly DispatcherTimer _revalidateTimer;
    private readonly List<Issue> _loadIssues;

    private NodeViewModel? _selectedNode;
    private bool _noticeDismissed;
    private int _bottomTabIndex;

    internal EditorViewModel(MainViewModel shell, RibbonDocument model, IEnumerable<Issue> loadIssues, bool needsRepair)
    {
        _shell = shell;
        _loadIssues = loadIssues.ToList();

        _revalidateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _revalidateTimer.Tick += (_, _) => { _revalidateTimer.Stop(); Revalidate(); };

        SaveCommand = new RelayCommand(() => Save());
        SaveAsCommand = new RelayCommand(() => SaveAs());
        CloseCommand = new RelayCommand(() => _shell.CloseTab(this));
        SelectDllCommand = new RelayCommand(SelectDll);
        RescanDllCommand = new RelayCommand(RescanDll, () => HasDll);
        DismissNoticeCommand = new RelayCommand(() => { _noticeDismissed = true; OnPropertyChanged(nameof(ShowDeployedNotice)); });

        Document = new DocumentViewModel(model, this);
        Document.Changed += OnDocumentChanged;
        Document.NodeSelected += OnNodeSelected;
        Document.PropertyChanged += OnDocumentPropertyChanged;

        if (needsRepair)
            Document.IsDirty = true;

        Document.Root.IsSelected = true;
        SelectedNode = Document.Root;

        AutoDetectDll();
        Revalidate();
    }

    // ---- state -----------------------------------------------------------------------

    public DocumentViewModel Document { get; }

    public NodeViewModel? SelectedNode
    {
        get => _selectedNode;
        private set => SetProperty(ref _selectedNode, value);
    }

    public ObservableCollection<IssueViewModel> Issues { get; } = new();

    public int ErrorCount => Issues.Count(i => i.IsError);
    public int WarningCount => Issues.Count(i => !i.IsError);
    public string IssueSummary => Issues.Count == 0 ? "No issues" : $"{ErrorCount} error(s), {WarningCount} warning(s)";

    /// <summary>Tab header: file name plus * when dirty.</summary>
    public string Header => Document.DisplayName + (Document.IsDirty ? "*" : string.Empty);

    public string StatusPath => Document.FilePath ?? "Not saved yet";
    public bool IsDeployedCopy => Document.FilePath?.StartsWith(DeployedRoot, StringComparison.OrdinalIgnoreCase) == true;
    public bool ShowDeployedNotice => IsDeployedCopy && !_noticeDismissed;
    public string DllStatusText => Document.DllStatusText;
    public bool HasDll => Document.DllPath is not null;

    /// <summary>Which bottom tab is showing: 0 = Issues, 1 = Ribbon preview. Kept here so it survives switching document tabs.</summary>
    public int BottomTabIndex
    {
        get => _bottomTabIndex;
        set => SetProperty(ref _bottomTabIndex, value);
    }

    // ---- commands --------------------------------------------------------------------

    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand CloseCommand { get; }
    public RelayCommand SelectDllCommand { get; }
    public RelayCommand RescanDllCommand { get; }
    public RelayCommand DismissNoticeCommand { get; }

    /// <summary>Asks the view to focus the editor for a field once the property panel has rendered.</summary>
    public event EventHandler<Fields.FieldViewModel>? FocusFieldRequested;

    // ---- saving ----------------------------------------------------------------------

    public bool Save() => Document.FilePath is null ? SaveAs() : SaveTo(Document.FilePath);

    public bool SaveAs()
    {
        var initial = Document.FilePath is { } p ? Path.GetDirectoryName(p) : _settings.LastOpenFolder;
        var path = Dialogs.SaveRibbon(initial, Document.DisplayName);
        return path is not null && SaveTo(path);
    }

    private bool SaveTo(string path)
    {
        if (_shell.IsOpenElsewhere(path, this))
        {
            Dialogs.Error($"{Path.GetFileName(path)} is already open in another tab. Close that tab first, or choose a different name.");
            return false;
        }

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
            _shell.RememberFile(path);
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
        if (!Document.IsDirty)
            return true;
        return Dialogs.YesNoCancel($"Save changes to {Document.DisplayName}?") switch
        {
            MessageBoxResult.Yes => Save(),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    /// <summary>Called by the shell when the tab is removed: stops timers and releases the document.</summary>
    internal void Detach()
    {
        _revalidateTimer.Stop();
        Document.Changed -= OnDocumentChanged;
        Document.NodeSelected -= OnNodeSelected;
        Document.PropertyChanged -= OnDocumentPropertyChanged;
        Document.ClosePreviewPopups();
    }

    // ---- tab DLL / command classes ---------------------------------------------------

    private void SelectDll()
    {
        var initial = Document.DllPath is { } d ? Path.GetDirectoryName(d)
                    : Document.FilePath is { } p ? Path.GetDirectoryName(p) : null;
        var picked = Dialogs.OpenDll(initial);
        if (picked is not null)
            SetDll(picked, remember: true);
    }

    private void RescanDll()
    {
        if (Document.DllPath is { } path)
            SetDll(path, remember: false);
    }

    private void AutoDetectDll()
    {
        if (Document.FilePath is not { } ribbon)
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
        Document.DllPath = dllPath;
        Document.ScannedClasses.Clear();
        var result = CommandClassScanner.Scan(dllPath);
        foreach (var c in result.ClassNames)
            Document.ScannedClasses.Add(c);

        // Pane classes may live in the tab DLL or in the Resources DLL next to it (the parser follows the tab DLL's references).
        Document.ScannedPaneClasses.Clear();
        Document.PaneDllNames.Clear();
        Document.PaneScanSucceeded = false;
        var paneClasses = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var paneDll in CommandClassScanner.PaneDllsFor(dllPath))
        {
            Document.PaneDllNames.Add(Path.GetFileName(paneDll));
            var panes = CommandClassScanner.ScanPaneProviders(paneDll);
            if (panes.Error is null)
                Document.PaneScanSucceeded = true;
            foreach (var p in panes.ClassNames)
                paneClasses.Add(p);
        }
        foreach (var p in paneClasses)
            Document.ScannedPaneClasses.Add(p);

        Document.NotifyClassesChanged();
        OnPropertyChanged(nameof(DllStatusText));
        OnPropertyChanged(nameof(HasDll));
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
        {
            OnPropertyChanged(nameof(DllStatusText));
            OnPropertyChanged(nameof(HasDll));
            RescanDllCommand.RaiseCanExecuteChanged();
        }
    }

    private void OnNodeSelected(object? sender, NodeViewModel node)
    {
        SelectedNode = node;
        node.RevealInPreview();
    }

    public void Revalidate()
    {
        _revalidateTimer.Stop();
        Issues.Clear();

        var context = new ValidationContext(
            KnownClasses: Document.ScannedClasses.Count > 0 ? new HashSet<string>(Document.ScannedClasses, StringComparer.Ordinal) : null,
            DllName: Document.DllPath is { } d ? Path.GetFileName(d) : null,
            ImagesFolder: _settings.EffectiveImagesFolder,
            KnownPaneClasses: Document.PaneScanSucceeded ? new HashSet<string>(Document.ScannedPaneClasses, StringComparer.Ordinal) : null);

        var all = _loadIssues.Concat(RibbonValidator.Validate(Document.Model, context)).ToList();

        var byNode = all.Where(i => i.Node is not null).ToLookup(i => i.Node!);
        Document.Root.ApplyIssues(byNode);

        var vmByNode = Document.Root.DescendantsAndSelf().ToDictionary(v => v.Node);
        foreach (var issue in all.OrderByDescending(i => i.IsError))
        {
            NodeViewModel? node = issue.Node is not null && vmByNode.TryGetValue(issue.Node, out var v) ? v : null;
            Issues.Add(new IssueViewModel(issue, node));
        }
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(IssueSummary));
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

    private void NotifyHeaderProperties()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(StatusPath));
        OnPropertyChanged(nameof(IsDeployedCopy));
        OnPropertyChanged(nameof(ShowDeployedNotice));
        OnPropertyChanged(nameof(DllStatusText));
    }
}
