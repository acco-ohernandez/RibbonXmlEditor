using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using RibbonXmlEditor.ViewModels;
using RibbonXmlEditor.ViewModels.Fields;

namespace RibbonXmlEditor.Views;

/// <summary>
/// The editor for one open file. The document TabControl keeps a single instance of this view and
/// re-points its DataContext at the active <see cref="EditorViewModel"/>, so nothing per-document is cached here.
/// </summary>
public partial class DocumentView : UserControl
{
    private EditorViewModel? _vm;

    public DocumentView()
    {
        InitializeComponent();

        DataContextChanged += (_, e) => Attach(e.NewValue as EditorViewModel);
        Loaded += (_, _) => Attach(DataContext as EditorViewModel);
        Unloaded += (_, _) => Attach(null);

        // A selection made from the ribbon preview or the issues list should scroll the tree to the item.
        Tree.AddHandler(TreeViewItem.SelectedEvent, new RoutedEventHandler((_, e) => (e.OriginalSource as TreeViewItem)?.BringIntoView()));
    }

    private void Attach(EditorViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm))
            return;
        if (_vm is not null)
            _vm.FocusFieldRequested -= OnFocusFieldRequested;
        _vm = vm;
        if (_vm is not null)
            _vm.FocusFieldRequested += OnFocusFieldRequested;
    }

    /// <summary>The property panel re-templates when the selection changes; wait for layout, then ask the field to focus.</summary>
    private void OnFocusFieldRequested(object? sender, FieldViewModel field)
        => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => field.RequestFocus());

    /// <summary>Right-click selects the item under the mouse so the context menu acts on it.</summary>
    private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item is not null)
        {
            item.IsSelected = true;
            item.Focus();
        }
    }

    private void IssuesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssuesList.SelectedItem is IssueViewModel issue)
            _vm?.NavigateTo(issue);
    }

    // ---- drag and drop in the tree ---------------------------------------------------
    //
    // A left-button drag on a row starts a Move drag of its NodeViewModel. While dragging, the row under the
    // mouse is split into an upper edge (drop before), a lower edge (drop after) and, for containers, a middle
    // (append inside). NodeViewModel.MoveBlockedReason decides whether the drop is legal; illegal drops show the
    // "not allowed" cursor and do nothing. An adorner draws the insertion line or the target outline.

    private const string DragFormat = "RibbonXmlEditor.NodeViewModel";
    private const double EdgeFraction = 0.25;

    private Point _dragStart;
    private NodeViewModel? _dragCandidate;
    private DropAdorner? _dropAdorner;

    private enum DropPlacement { Before, After, Into }

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragCandidate = null;
        var origin = e.OriginalSource as DependencyObject;
        if (FindAncestor<System.Windows.Controls.Primitives.ToggleButton>(origin) is not null)
            return; // the expand/collapse arrow
        var item = FindAncestor<TreeViewItem>(origin);
        if (item?.DataContext is NodeViewModel { Parent: not null } vm)
        {
            _dragCandidate = vm;
            _dragStart = e.GetPosition(Tree);
        }
    }

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _dragCandidate = null;
            return;
        }
        var delta = e.GetPosition(Tree) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var source = _dragCandidate;
        _dragCandidate = null;
        try
        {
            DragDrop.DoDragDrop(Tree, new DataObject(DragFormat, source), DragDropEffects.Move);
        }
        finally
        {
            ClearDropAdorner();
        }
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        AutoScroll(e);
        if (TryResolveDrop(e, out _, out _, out _, out var item, out var placement))
        {
            e.Effects = DragDropEffects.Move;
            ShowDropAdorner(item, placement);
        }
        else
        {
            e.Effects = DragDropEffects.None;
            ClearDropAdorner();
        }
    }

    private void Tree_DragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only clear when the mouse really left the tree.
        var p = e.GetPosition(Tree);
        if (p.X < 0 || p.Y < 0 || p.X > Tree.ActualWidth || p.Y > Tree.ActualHeight)
            ClearDropAdorner();
    }

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearDropAdorner();
        if (TryResolveDrop(e, out var source, out var parent, out var index, out _, out _))
        {
            source.MoveTo(parent, index);
            Tree.Focus();
        }
    }

    /// <summary>Works out the (parent, index) a drop at the mouse position means, and whether the source may go there.</summary>
    private bool TryResolveDrop(DragEventArgs e, out NodeViewModel source, out NodeViewModel parent, out int index,
        out TreeViewItem item, out DropPlacement placement)
    {
        source = null!; parent = null!; index = -1; item = null!; placement = DropPlacement.After;

        if (e.Data.GetData(DragFormat) is not NodeViewModel dragged || dragged.Parent is null)
            return false;
        var hit = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (hit?.DataContext is not NodeViewModel target)
            return false;

        var header = HeaderOf(hit);
        var y = e.GetPosition(header).Y;
        double h = Math.Max(header.ActualHeight, 1);

        if (target.CanAddChildren)
            placement = y < h * EdgeFraction ? DropPlacement.Before : y > h * (1 - EdgeFraction) ? DropPlacement.After : DropPlacement.Into;
        else
            placement = y < h / 2 ? DropPlacement.Before : DropPlacement.After;

        NodeViewModel? newParent;
        int at;
        switch (placement)
        {
            case DropPlacement.Into:
                newParent = target;
                at = target.Children.Count;
                break;
            default:
                newParent = target.Parent;
                at = target.Node.IndexInParent + (placement == DropPlacement.After ? 1 : 0);
                break;
        }
        if (newParent is null || ReferenceEquals(target, dragged))
            return false;
        if (dragged.MoveBlockedReason(newParent, at) is not null)
            return false;

        // Dropping right where it already sits is a no-op: show nothing rather than a misleading line.
        if (ReferenceEquals(newParent, dragged.Parent))
        {
            int from = dragged.Node.IndexInParent;
            if (at == from || at == from + 1)
                return false;
        }

        source = dragged; parent = newParent; index = at; item = hit;
        return true;
    }

    private static FrameworkElement HeaderOf(TreeViewItem item)
        => item.Template?.FindName("PART_Header", item) as FrameworkElement ?? item;

    private void ShowDropAdorner(TreeViewItem item, DropPlacement placement)
    {
        var header = HeaderOf(item);
        if (_dropAdorner is not null && ReferenceEquals(_dropAdorner.AdornedElement, header))
        {
            _dropAdorner.Placement = placement;
            return;
        }
        ClearDropAdorner();
        var layer = AdornerLayer.GetAdornerLayer(header);
        if (layer is null)
            return;
        _dropAdorner = new DropAdorner(header) { Placement = placement };
        layer.Add(_dropAdorner);
    }

    private void ClearDropAdorner()
    {
        if (_dropAdorner is null)
            return;
        AdornerLayer.GetAdornerLayer(_dropAdorner.AdornedElement)?.Remove(_dropAdorner);
        _dropAdorner = null;
    }

    /// <summary>
    /// Scrolls the tree while dragging near its top or bottom edge, but never while the pointer is still on the
    /// row the drag started from (a drag that begins on the last visible row must not scroll away from it).
    /// </summary>
    private void AutoScroll(DragEventArgs e)
    {
        if (FindDescendant<ScrollViewer>(Tree) is not { } sv)
            return;
        var y = e.GetPosition(Tree).Y;
        const double zone = 24;
        if (y >= zone && y <= Tree.ActualHeight - zone)
            return;
        if (e.Data.GetData(DragFormat) is NodeViewModel dragged
            && FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext is NodeViewModel under
            && ReferenceEquals(under, dragged))
            return;
        if (y < zone) sv.LineUp();
        else sv.LineDown();
    }

    private static T? FindDescendant<T>(DependencyObject start) where T : DependencyObject
    {
        int n = VisualTreeHelper.GetChildrenCount(start);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(start, i);
            if (child is T t)
                return t;
            if (FindDescendant<T>(child) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>Insertion line above or below a row, or an outline around a container the item will be appended to.</summary>
    private sealed class DropAdorner : Adorner
    {
        private static readonly Brush LineBrush = new SolidColorBrush(Color.FromRgb(0x1E, 0x88, 0xE5));
        private static readonly Pen LinePen = new(LineBrush, 2);
        private static readonly Pen OutlinePen = new(LineBrush, 1.5);
        private DropPlacement _placement;

        static DropAdorner()
        {
            LineBrush.Freeze();
            LinePen.Freeze();
            OutlinePen.Freeze();
        }

        public DropAdorner(UIElement adorned) : base(adorned)
        {
            IsHitTestVisible = false;
        }

        public DropPlacement Placement
        {
            get => _placement;
            set
            {
                if (_placement == value) return;
                _placement = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var size = AdornedElement.RenderSize;
            switch (_placement)
            {
                case DropPlacement.Into:
                    dc.DrawRoundedRectangle(null, OutlinePen, new Rect(0.75, 0.75, size.Width - 1.5, size.Height - 1.5), 3, 3);
                    break;
                default:
                    double y = _placement == DropPlacement.Before ? 0 : size.Height;
                    dc.DrawLine(LinePen, new Point(0, y), new Point(size.Width, y));
                    // a small triangle at the left end marks the insertion point
                    var tri = new StreamGeometry();
                    using (var g = tri.Open())
                    {
                        g.BeginFigure(new Point(0, y - 4), true, true);
                        g.LineTo(new Point(5, y), false, false);
                        g.LineTo(new Point(0, y + 4), false, false);
                    }
                    tri.Freeze();
                    dc.DrawGeometry(LineBrush, null, tri);
                    break;
            }
        }
    }

    private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
    {
        for (var d = start; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is T t)
                return t;
        }
        return null;
    }
}
