using System.Collections.ObjectModel;
using System.Windows.Media;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.Services;
using RibbonXmlEditor.ViewModels.Fields;

namespace RibbonXmlEditor.ViewModels;

/// <summary>Tree node wrapper around a <see cref="RibbonNode"/>: fields, children and structural commands.</summary>
public sealed class NodeViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isExpanded = true;
    private bool _hasError;
    private bool _hasWarning;

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

    /// <summary>Node-level issues (not tied to a single attribute).</summary>
    public ObservableCollection<Issue> NodeIssues { get; } = new();

    // ---- presentation ----------------------------------------------------------------

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

    public IEnumerable<NodeViewModel> DescendantsAndSelf()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.DescendantsAndSelf())
                yield return d;
    }

    public void ExpandAncestors()
    {
        for (var p = Parent; p is not null; p = p.Parent)
            p.IsExpanded = true;
    }

    // ---- change notifications --------------------------------------------------------

    internal void OnAttributeChanged(string xmlName)
    {
        if (xmlName is "name" or "text")
            OnPropertyChanged(nameof(Header));
        Doc.MarkChanged();
    }

    private void ChildrenChanged()
    {
        OnPropertyChanged(nameof(Header));
        foreach (var o in AddChildOptions)
            o.Refresh();
        foreach (var c in Children)
        {
            c.MoveUpCommand.RaiseCanExecuteChanged();
            c.MoveDownCommand.RaiseCanExecuteChanged();
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
