using System.Windows;
using System.Windows.Controls;
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
