using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using RibbonXmlEditor.ViewModels;
using RibbonXmlEditor.ViewModels.Fields;

namespace RibbonXmlEditor.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // A selection made from the ribbon preview or the issues list should scroll the tree to the item.
        Tree.AddHandler(TreeViewItem.SelectedEvent, new RoutedEventHandler((_, e) => (e.OriginalSource as TreeViewItem)?.BringIntoView()));
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.RequestClose -= OnRequestClose;
            _vm.FocusFieldRequested -= OnFocusFieldRequested;
        }
        _vm = e.NewValue as MainViewModel;
        if (_vm is not null)
        {
            _vm.RequestClose += OnRequestClose;
            _vm.FocusFieldRequested += OnFocusFieldRequested;
        }
    }

    private void OnRequestClose(object? sender, EventArgs e) => Close();

    /// <summary>The property panel re-templates when the selection changes; wait for layout, then ask the field to focus.</summary>
    private void OnFocusFieldRequested(object? sender, FieldViewModel field)
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => field.RequestFocus());
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm is not null && !_vm.CanClose())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

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

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        var ribbon = files.FirstOrDefault(f => f.EndsWith(".ribbon", StringComparison.OrdinalIgnoreCase));
        if (ribbon is not null)
            _vm?.OpenFile(ribbon);
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
