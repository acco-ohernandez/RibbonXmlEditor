using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.Services;
using RibbonXmlEditor.ViewModels.Fields;

namespace RibbonXmlEditor.ViewModels;

/// <summary>Tree node wrapper around a <see cref="RibbonNode"/>: fields, children, structural commands and preview state.</summary>
public sealed class NodeViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isExpanded = true;
    private bool _hasError;
    private bool _hasWarning;
    private bool _hasOwnError;
    private bool _hasOwnWarning;
    private bool _isPreviewExpanded;
    private BitmapSource? _smallImage;
    private BitmapSource? _largeImage;
    private bool _smallImageLoaded;
    private bool _largeImageLoaded;

    public NodeViewModel(RibbonNode node, DocumentViewModel doc, NodeViewModel? parent)
    {
        Node = node;
        Doc = doc;
        Parent = parent;

        Fields = node.Def.Attributes.Select(a => FieldViewModel.Create(this, a)).ToList();
        Children = new ObservableCollection<NodeViewModel>(node.Children.Select(c => new NodeViewModel(c, doc, this)));
        AddChildOptions = node.Def.AllowedChildren.Select(k => new AddChildOption(this, k)).ToList();

        DeleteCommand = new RelayCommand(Delete, () => Parent is not null);
        DuplicateCommand = new RelayCommand(Duplicate, () => Parent is not null);
        MoveUpCommand = new RelayCommand(() => Move(-1), () => Parent is not null && Node.IndexInParent > 0);
        MoveDownCommand = new RelayCommand(() => Move(+1), () => Parent is not null && Node.IndexInParent < Parent.Children.Count - 1);
        SelectCommand = new RelayCommand(SelectFromPreview);
    }

    public RibbonNode Node { get; }
    public DocumentViewModel Doc { get; }
    public NodeViewModel? Parent { get; }
    public ObservableCollection<NodeViewModel> Children { get; }
    public IReadOnlyList<FieldViewModel> Fields { get; }
    public IReadOnlyList<AddChildOption> AddChildOptions { get; }

    public ElementDef Def => Node.Def;
    public bool IsTab => Node.Kind == ElementKind.Tab;
    public string KindName => Def.DisplayName;
    public string Description => Def.Description;
    public bool CanAddChildren => Def.CanHaveChildren;

    public RelayCommand DeleteCommand { get; }
    public RelayCommand DuplicateCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    /// <summary>Selects this node from the ribbon preview: expands the tree path and closes the parent's drop list.</summary>
    public RelayCommand SelectCommand { get; }

    /// <summary>Node-level issues (not tied to a single attribute).</summary>
    public ObservableCollection<Issue> NodeIssues { get; } = new();

    // ---- presentation (tree) ---------------------------------------------------------

    public string Header
    {
        get
        {
            var name = Node.Name;
            if (Def.HasAttribute("name") && name.Length > 0)
                return $"{Def.DisplayName}: {name}";
            if (Node.Kind == ElementKind.Button && Node["text"].Length > 0)
                return $"{Def.DisplayName}: {Node["text"].Replace('\n', ' ')}";
            if (Def.CanHaveChildren)
            {
                return Def.MaxChildren == RibbonSchema.Unbounded
                    ? $"{Def.DisplayName} ({Children.Count})"
                    : $"{Def.DisplayName} ({Children.Count}/{Def.MaxChildren})";
            }
            return Def.DisplayName;
        }
    }

    public string Glyph => Node.Kind switch
    {
        ElementKind.Tab => "TAB",
        ElementKind.Panel => "PNL",
        ElementKind.Separator => "|",
        ElementKind.StackedItems => "≡",
        ElementKind.SplitButtons => "⊟",
        ElementKind.SlideoutPanel => "▾",
        ElementKind.RadioButtons => "◉",
        ElementKind.Button => "B",
        ElementKind.PulldownButtons => "▼",
        ElementKind.ComboBox => "☰",
        ElementKind.TextBox => "▭",
        ElementKind.ComboBoxMember => "•",
        ElementKind.ToggleButton => "◐",
        _ => "?",
    };

    public Brush GlyphBrush => Node.Kind switch
    {
        ElementKind.Tab => Brushes.DarkSlateBlue,
        ElementKind.Panel => Brushes.SteelBlue,
        ElementKind.Separator => Brushes.Gray,
        ElementKind.StackedItems => Brushes.CadetBlue,
        ElementKind.SplitButtons or ElementKind.SlideoutPanel or ElementKind.RadioButtons => Brushes.DarkCyan,
        ElementKind.Button => Brushes.SeaGreen,
        ElementKind.PulldownButtons => Brushes.DarkOliveGreen,
        ElementKind.ComboBox or ElementKind.TextBox => Brushes.Peru,
        ElementKind.ComboBoxMember or ElementKind.ToggleButton => Brushes.DarkKhaki,
        _ => Brushes.Gray,
    };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value) && value)
                Doc.NotifySelected(this);
        }
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>True when this node or any descendant has an error.</summary>
    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    /// <summary>True when this node or any descendant has a warning (and no error).</summary>
    public bool HasWarning
    {
        get => _hasWarning;
        private set => SetProperty(ref _hasWarning, value);
    }

    /// <summary>True when this node itself (not a descendant) has an error.</summary>
    public bool HasOwnError
    {
        get => _hasOwnError;
        private set => SetProperty(ref _hasOwnError, value);
    }

    /// <summary>True when this node itself has a warning and no own error.</summary>
    public bool HasOwnWarning
    {
        get => _hasOwnWarning;
        private set => SetProperty(ref _hasOwnWarning, value);
    }

    // ---- presentation (ribbon preview) -----------------------------------------------

    /// <summary>Text shown in the ribbon preview. Buttons show their caption; containers show their name.</summary>
    public string Caption
    {
        get
        {
            var name = Node.Name;
            switch (Node.Kind)
            {
                case ElementKind.Button or ElementKind.ToggleButton or ElementKind.ComboBoxMember:
                    return FirstNonEmpty(Node["text"], name, "(no caption)");
                case ElementKind.ComboBox:
                    return FirstNonEmpty(Node["itemtext"], name, "(combo box)");
                case ElementKind.TextBox:
                    return FirstNonEmpty(Node["prompttext"], name, "(text box)");
                case ElementKind.Separator or ElementKind.StackedItems or ElementKind.SplitButtons:
                    return Def.DisplayName;
                default:
                    return FirstNonEmpty(name, string.Empty, "(unnamed)");
            }
        }
    }

    public string CaptionSingleLine => Caption.Replace('\n', ' ');

    /// <summary>16 px image from the <c>image</c> attribute, or null when empty or missing. Cached until the attribute changes.</summary>
    public BitmapSource? SmallImage
    {
        get
        {
            if (!_smallImageLoaded)
            {
                _smallImage = ImageCatalog.LoadThumbnail(Node["image"], 16);
                _smallImageLoaded = true;
            }
            return _smallImage;
        }
    }

    /// <summary>32 px image: the pulldown's <c>image</c>, otherwise <c>largeimage</c>. Cached until the attribute changes.</summary>
    public BitmapSource? LargeImage
    {
        get
        {
            if (!_largeImageLoaded)
            {
                var attr = Node.Kind == ElementKind.PulldownButtons ? "image" : "largeimage";
                _largeImage = ImageCatalog.LoadThumbnail(Node[attr], 32);
                _largeImageLoaded = true;
            }
            return _largeImage;
        }
    }

    public bool HasSmallImage => SmallImage is not null;
    public bool HasLargeImage => LargeImage is not null;

    /// <summary>Large (icon over caption) versus small (icon beside caption) rendering, following Revit's rules.</summary>
    public bool IsLargeInPreview => Parent?.Node.Kind switch
    {
        ElementKind.StackedItems => Parent.Children.Count == 1,
        ElementKind.SplitButtons or ElementKind.SlideoutPanel or ElementKind.RadioButtons => true,
        _ => false,
    };

    /// <summary>The split button's face.</summary>
    public NodeViewModel? FirstChild => Children.FirstOrDefault();

    /// <summary>A panel's slide-out (Revit merges several into one).</summary>
    public NodeViewModel? Slideout => Children.FirstOrDefault(c => c.Node.Kind == ElementKind.SlideoutPanel);

    /// <summary>Drop list open (pulldown, split, combo) or slide-out row visible.</summary>
    public bool IsPreviewExpanded
    {
        get => _isPreviewExpanded;
        set => SetProperty(ref _isPreviewExpanded, value);
    }

    /// <summary>Makes this node visible in the preview by opening any slide-out that contains it.</summary>
    public void RevealInPreview()
    {
        foreach (var a in Ancestors())
        {
            if (a.Node.Kind == ElementKind.SlideoutPanel)
                a.IsPreviewExpanded = true;
        }
    }

    internal void NotifyPreviewLayoutChanged() => OnPropertyChanged(nameof(IsLargeInPreview));

    private void SelectFromPreview()
    {
        ExpandAncestors();
        IsSelected = true;
        if (Parent?.Node.Kind is ElementKind.PulldownButtons or ElementKind.SplitButtons or ElementKind.ComboBox)
            Parent.IsPreviewExpanded = false;
    }

    private static string FirstNonEmpty(string a, string b, string fallback)
        => a.Length > 0 ? a : b.Length > 0 ? b : fallback;

    // ---- validation plumbing ---------------------------------------------------------

    /// <summary>Distributes issues to fields and node, then rolls flags up. Returns (hasError, hasWarning) including descendants.</summary>
    public (bool error, bool warning) ApplyIssues(ILookup<RibbonNode, Issue> byNode)
    {
        var mine = byNode[Node].ToList();
        foreach (var f in Fields)
            f.SetIssues(mine.Where(i => i.AttributeName == f.XmlName));

        NodeIssues.Clear();
        foreach (var i in mine.Where(i => i.AttributeName is null || !Def.HasAttribute(i.AttributeName)))
            NodeIssues.Add(i);

        bool error = mine.Any(i => i.IsError);
        bool warning = mine.Any(i => !i.IsError);
        HasOwnError = error;
        HasOwnWarning = !error && warning;

        foreach (var c in Children)
        {
            var (ce, cw) = c.ApplyIssues(byNode);
            error |= ce;
            warning |= cw;
        }
        HasError = error;
        HasWarning = !error && warning;
        return (error, warning);
    }

    public FieldViewModel? GetField(string xmlName) => Fields.FirstOrDefault(f => f.XmlName == xmlName);

    public IEnumerable<NodeViewModel> Ancestors()
    {
        for (var p = Parent; p is not null; p = p.Parent)
            yield return p;
    }

    public IEnumerable<NodeViewModel> DescendantsAndSelf()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.DescendantsAndSelf())
                yield return d;
    }

    public void ExpandAncestors()
    {
        foreach (var a in Ancestors())
            a.IsExpanded = true;
    }

    // ---- change notifications --------------------------------------------------------

    internal void OnAttributeChanged(string xmlName)
    {
        switch (xmlName)
        {
            case "name" or "text" or "itemtext" or "prompttext":
                OnPropertyChanged(nameof(Header));
                OnPropertyChanged(nameof(Caption));
                OnPropertyChanged(nameof(CaptionSingleLine));
                break;
            case "image" or "largeimage":
                _smallImageLoaded = false;
                _largeImageLoaded = false;
                _smallImage = null;
                _largeImage = null;
                OnPropertyChanged(nameof(SmallImage));
                OnPropertyChanged(nameof(LargeImage));
                OnPropertyChanged(nameof(HasSmallImage));
                OnPropertyChanged(nameof(HasLargeImage));
                break;
        }
        Doc.MarkChanged();
    }

    private void ChildrenChanged()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(FirstChild));
        OnPropertyChanged(nameof(Slideout));
        foreach (var o in AddChildOptions)
            o.Refresh();
        foreach (var c in Children)
        {
            c.MoveUpCommand.RaiseCanExecuteChanged();
            c.MoveDownCommand.RaiseCanExecuteChanged();
            c.NotifyPreviewLayoutChanged();
        }
        Doc.MarkChanged();
    }

    // ---- structural commands ---------------------------------------------------------

    public NodeViewModel AddChild(ElementKind kind, int index = -1)
    {
        var model = new RibbonNode(kind);
        Node.AddChild(model, index);
        var vm = new NodeViewModel(model, Doc, this);
        if (index < 0 || index >= Children.Count)
            Children.Add(vm);
        else
            Children.Insert(index, vm);
        IsExpanded = true;
        ChildrenChanged();
        vm.IsSelected = true;
        return vm;
    }

    private void Delete()
    {
        if (Parent is null)
            return;
        if (Children.Count > 0 && !Dialogs.Confirm($"Delete \"{Header}\" and the {Children.Count} item(s) inside it?"))
            return;

        var parent = Parent;
        int index = Node.IndexInParent;
        parent.Node.RemoveChild(Node);
        parent.Children.Remove(this);
        parent.ChildrenChanged();

        var next = parent.Children.Count > 0 ? parent.Children[Math.Min(index, parent.Children.Count - 1)] : parent;
        next.IsSelected = true;
    }

    private void Duplicate()
    {
        if (Parent is null)
            return;
        var clone = Node.DeepClone();
        if (clone.Def.HasAttribute("name") && clone.Name.Length > 0)
            clone["name"] = clone.Name + " Copy";

        int index = Node.IndexInParent + 1;
        Parent.Node.AddChild(clone, index);
        var vm = new NodeViewModel(clone, Doc, Parent);
        Parent.Children.Insert(index, vm);
        Parent.ChildrenChanged();
        vm.IsSelected = true;
    }

    private void Move(int delta)
    {
        if (Parent is null)
            return;
        int from = Node.IndexInParent;
        if (!Parent.Node.MoveChild(Node, delta))
            return;
        Parent.Children.Move(from, from + delta);
        Parent.ChildrenChanged();
        IsSelected = true;
    }

    /// <summary>
    /// After the user picks a 16x16 image, offer to fill the matching 32x32 / 192x192 siblings
    /// that are still empty (ACCO naming convention Name_16x16.png / Name_32x32.png / Name_192x192.png).
    /// </summary>
    internal void OfferSiblingImages(ImagePathField source, string pickedPath)
    {
        var fills = new List<(ImagePathField field, string path)>();
        foreach (var f in Fields.OfType<ImagePathField>())
        {
            if (ReferenceEquals(f, source) || f.Value.Length > 0)
                continue;
            var sibling = ImageCatalog.SiblingFor(pickedPath, f.XmlName);
            if (sibling is not null)
                fills.Add((f, sibling));
        }
        if (fills.Count == 0)
            return;

        var list = string.Join("\n", fills.Select(x => $"  {x.field.Label}: {Path.GetFileName(x.path)}"));
        if (Dialogs.Confirm($"Matching images were found for the empty fields. Fill them in?\n\n{list}"))
        {
            foreach (var (field, path) in fills)
                field.Value = path;
        }
    }
}

/// <summary>One entry of the "Add" menu: a child kind the node accepts.</summary>
public sealed class AddChildOption : ObservableObject
{
    public AddChildOption(NodeViewModel owner, ElementKind kind)
    {
        Owner = owner;
        Kind = kind;
        DisplayName = RibbonSchema.ByKind(kind).DisplayName;
        Command = new RelayCommand(() => Owner.AddChild(Kind), () => IsEnabled);
    }

    public NodeViewModel Owner { get; }
    public ElementKind Kind { get; }
    public string DisplayName { get; }
    public string ButtonText => $"Add {DisplayName.ToLowerInvariant()}";
    public RelayCommand Command { get; }
    public bool IsEnabled => Owner.Children.Count < Owner.Def.MaxChildren;

    public void Refresh()
    {
        OnPropertyChanged(nameof(IsEnabled));
        Command.RaiseCanExecuteChanged();
    }
}
