using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Media.Imaging;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.Services;

namespace RibbonXmlEditor.ViewModels.Fields;

/// <summary>One editable attribute of a node. Subclasses select the DataTemplate used in the property panel.</summary>
public abstract class FieldViewModel : ObservableObject
{
    protected FieldViewModel(NodeViewModel owner, AttributeDef def)
    {
        Owner = owner;
        Def = def;
    }

    public NodeViewModel Owner { get; }
    public AttributeDef Def { get; }
    public string XmlName => Def.XmlName;
    public string Label => Def.Label;
    public string Hint => Def.Hint;
    public bool IsRequired => Def.IsRequired;

    /// <summary>The attribute value. Line breaks are normalised to '\n' before reaching the model.</summary>
    public string Value
    {
        get => Owner.Node[XmlName];
        set
        {
            var normalized = (value ?? string.Empty)
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');
            if (normalized == Owner.Node[XmlName])
                return;
            Owner.Node[XmlName] = normalized;
            OnPropertyChanged();
            OnValueChanged();
            Owner.OnAttributeChanged(XmlName);
        }
    }

    protected virtual void OnValueChanged() { }

    // ---- validation state, filled by MainViewModel after each validation pass ----------

    public ObservableCollection<Issue> Issues { get; } = new();
    public bool HasIssues => Issues.Count > 0;
    public bool HasError => Issues.Any(i => i.IsError);
    public bool HasWarning => !HasError && Issues.Any(i => !i.IsError);
    public string IssueText => string.Join("\n", Issues.Select(i => i.Message));

    public void SetIssues(IEnumerable<Issue> issues)
    {
        Issues.Clear();
        foreach (var i in issues)
            Issues.Add(i);
        OnPropertyChanged(nameof(HasIssues));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(HasWarning));
        OnPropertyChanged(nameof(IssueText));
    }

    // ---- focus (issue list navigation) ------------------------------------------------

    public event EventHandler? FocusRequested;
    public void RequestFocus() => FocusRequested?.Invoke(this, EventArgs.Empty);

    public static FieldViewModel Create(NodeViewModel owner, AttributeDef def) => def.Kind switch
    {
        FieldKind.Text => new TextField(owner, def),
        FieldKind.MultilineText => new MultilineTextField(owner, def),
        FieldKind.ImagePath => new ImagePathField(owner, def),
        FieldKind.ClassName => new ClassNameField(owner, def),
        FieldKind.Url => new UrlField(owner, def),
        FieldKind.TrueOrEmpty => new TrueOrEmptyField(owner, def),
        FieldKind.Guid => new GuidField(owner, def),
        FieldKind.PaneClassName => new PaneClassNameField(owner, def),
        _ => throw new NotSupportedException($"Unknown field kind {def.Kind}."),
    };
}

/// <summary>A GUID with a "New" button. Any format Guid.TryParse accepts is valid; New writes the upper-case D form.</summary>
public sealed class GuidField : FieldViewModel
{
    public GuidField(NodeViewModel owner, AttributeDef def) : base(owner, def)
    {
        NewGuidCommand = new RelayCommand(NewGuid);
    }

    public RelayCommand NewGuidCommand { get; }

    public bool IsValid => Value.Length == 0 || Guid.TryParse(Value.Trim(), out _);
    public bool IsInvalid => !IsValid;

    protected override void OnValueChanged()
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(IsInvalid));
    }

    private void NewGuid()
    {
        if (Value.Trim().Length > 0 && Guid.TryParse(Value.Trim(), out var current) && current != Guid.Empty
            && !Dialogs.Confirm("Replace the current GUID with a new one?\n\nRevit identifies the pane (and remembers its docked position) by this value, so a deployed pane gets a fresh identity."))
            return;
        Value = Guid.NewGuid().ToString("D").ToUpperInvariant();
    }
}

/// <summary>Pane class name with suggestions from the IDockablePaneProvider scan of the tab DLL and the Resources DLL next to it.</summary>
public sealed class PaneClassNameField : FieldViewModel
{
    public PaneClassNameField(NodeViewModel owner, AttributeDef def) : base(owner, def) { }

    public ObservableCollection<string> Suggestions => Owner.Doc.ScannedPaneClasses;
    public DocumentViewModel Doc => Owner.Doc;
}

public sealed class TextField : FieldViewModel
{
    public TextField(NodeViewModel owner, AttributeDef def) : base(owner, def) { }
}

public sealed class MultilineTextField : FieldViewModel
{
    public MultilineTextField(NodeViewModel owner, AttributeDef def) : base(owner, def) { }
}

public sealed class UrlField : FieldViewModel
{
    public UrlField(NodeViewModel owner, AttributeDef def) : base(owner, def)
    {
        OpenCommand = new RelayCommand(Open, () => IsValidUrl);
    }

    public RelayCommand OpenCommand { get; }

    public bool IsValidUrl => Uri.TryCreate(Value, UriKind.Absolute, out var u)
                              && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    protected override void OnValueChanged()
    {
        OnPropertyChanged(nameof(IsValidUrl));
        OpenCommand.RaiseCanExecuteChanged();
    }

    private void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Value) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Could not open the URL.\n\n{ex.Message}");
        }
    }
}

public sealed class TrueOrEmptyField : FieldViewModel
{
    public TrueOrEmptyField(NodeViewModel owner, AttributeDef def) : base(owner, def) { }

    /// <summary>RibbonBuilder only tests for the literal "true"; anything else means false.</summary>
    public bool IsChecked
    {
        get => Value == "true";
        set => Value = value ? "true" : string.Empty;
    }

    protected override void OnValueChanged() => OnPropertyChanged(nameof(IsChecked));
}

public sealed class ImagePathField : FieldViewModel
{
    public ImagePathField(NodeViewModel owner, AttributeDef def) : base(owner, def)
    {
        BrowseCommand = new RelayCommand(Browse);
        ClearCommand = new RelayCommand(() => Value = string.Empty, () => Value.Length > 0);
    }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand ClearCommand { get; }

    public bool IsMissing => Value.Length > 0 && !File.Exists(Value);
    public bool FileExists => Value.Length > 0 && File.Exists(Value);
    public BitmapSource? Thumbnail => ImageCatalog.LoadThumbnail(Value, 32);

    protected override void OnValueChanged()
    {
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(FileExists));
        OnPropertyChanged(nameof(Thumbnail));
        ClearCommand.RaiseCanExecuteChanged();
    }

    private void Browse()
    {
        var current = Value;
        string? initialDir = null;
        if (current.Length > 0)
        {
            try { initialDir = Path.GetDirectoryName(current); } catch { /* ignore bad paths */ }
        }
        if (initialDir is null || !Directory.Exists(initialDir))
            initialDir = Directory.Exists(AppSettings.Current.EffectiveImagesFolder) ? AppSettings.Current.EffectiveImagesFolder : null;

        var picked = Dialogs.OpenImage(initialDir);
        if (picked is null)
            return;

        Value = picked;
        Owner.OfferSiblingImages(this, picked);
    }
}

public sealed class ClassNameField : FieldViewModel
{
    public ClassNameField(NodeViewModel owner, AttributeDef def) : base(owner, def) { }

    public ObservableCollection<string> Suggestions => Owner.Doc.ScannedClasses;
    public DocumentViewModel Doc => Owner.Doc;
}
