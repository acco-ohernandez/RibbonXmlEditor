using System.Collections.ObjectModel;
using RibbonXmlEditor.Models;

namespace RibbonXmlEditor.ViewModels;

/// <summary>An open .ribbon file: the node tree plus the file header (version comment) and DLL context.</summary>
public sealed class DocumentViewModel : ObservableObject
{
    private bool _isDirty;
    private string? _dllPath;

    public DocumentViewModel(RibbonDocument model, EditorViewModel editor)
    {
        Model = model;
        Editor = editor;
        Root = new NodeViewModel(model.Tab, this, null);
        RootItems = new ObservableCollection<NodeViewModel> { Root };
        SetTodayCommand = new RelayCommand(() => VersionDate = DateTime.Today);
    }

    public RibbonDocument Model { get; }
    public EditorViewModel Editor { get; }
    public NodeViewModel Root { get; }
    public ObservableCollection<NodeViewModel> RootItems { get; }

    public string? FilePath => Model.FilePath;
    public string DisplayName => FilePath is null ? "Untitled.ribbon" : Path.GetFileName(FilePath);

    // ---- file header -----------------------------------------------------------------

    public string Version
    {
        get => Model.Version;
        set
        {
            var v = (value ?? string.Empty).Trim();
            if (v == Model.Version) return;
            Model.Version = v;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public DateTime? VersionDate
    {
        get => Model.VersionDate?.ToDateTime(TimeOnly.MinValue);
        set
        {
            DateOnly? d = value is null ? null : DateOnly.FromDateTime(value.Value);
            if (d == Model.VersionDate) return;
            Model.VersionDate = d;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public RelayCommand SetTodayCommand { get; }

    public IReadOnlyList<string> OtherComments => Model.OtherLeadingComments.Select(c => c.Trim()).ToList();
    public bool HasOtherComments => Model.OtherLeadingComments.Count > 0;

    // ---- dirty tracking --------------------------------------------------------------

    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    /// <summary>Raised on every model change (attribute edit, add, delete, move). Used to debounce validation.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when a tree node becomes selected.</summary>
    public event EventHandler<NodeViewModel>? NodeSelected;

    internal void MarkChanged()
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void NotifySelected(NodeViewModel node) => NodeSelected?.Invoke(this, node);

    public void NotifyPathChanged()
    {
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(DisplayName));
    }

    // ---- tab DLL / command classes ---------------------------------------------------

    public string? DllPath
    {
        get => _dllPath;
        set
        {
            if (SetProperty(ref _dllPath, value))
                OnPropertyChanged(nameof(DllStatusText));
        }
    }

    public ObservableCollection<string> ScannedClasses { get; } = new();

    /// <summary>IDockablePaneProvider classes from the tab DLL plus the Resources DLL next to it (see <see cref="PaneDllNames"/>).</summary>
    public ObservableCollection<string> ScannedPaneClasses { get; } = new();

    /// <summary>File names of the DLLs that were scanned for pane classes; empty when no DLL is selected.</summary>
    public List<string> PaneDllNames { get; } = new();

    /// <summary>True when at least one DLL was scanned for pane classes without error, so an unknown pane class is worth a warning.</summary>
    public bool PaneScanSucceeded { get; set; }

    public string DllStatusText => DllPath is null
        ? "No tab DLL selected. Class names are free text."
        : $"{Path.GetFileName(DllPath)}: {ScannedClasses.Count} command class(es), {ScannedPaneClasses.Count} pane class(es)";

    public string PaneDllStatusText => DllPath is null
        ? "No tab DLL selected. Pane class names are free text."
        : PaneDllNames.Count > 1
            ? $"{string.Join(" + ", PaneDllNames)}: {ScannedPaneClasses.Count} pane class(es)"
            : $"{string.Join(" + ", PaneDllNames)}: {ScannedPaneClasses.Count} pane class(es) ({Services.CommandClassScanner.ResourcesDllName} not found next to it)";

    public void NotifyClassesChanged()
    {
        OnPropertyChanged(nameof(DllStatusText));
        OnPropertyChanged(nameof(PaneDllStatusText));
    }

    public RelayCommand SelectDllCommand => Editor.SelectDllCommand;

    // ---- ribbon preview --------------------------------------------------------------

    /// <summary>Closes every open drop list in the preview (used when the preview tab is hidden).</summary>
    public void ClosePreviewPopups()
    {
        foreach (var n in Root.DescendantsAndSelf())
        {
            if (n.Node.Kind is Schema.ElementKind.PulldownButtons or Schema.ElementKind.SplitButtons or Schema.ElementKind.ComboBox)
                n.IsPreviewExpanded = false;
        }
    }

    // ---- lookup ----------------------------------------------------------------------

    public NodeViewModel? FindNode(RibbonNode node)
        => Root.DescendantsAndSelf().FirstOrDefault(v => ReferenceEquals(v.Node, node));
}
