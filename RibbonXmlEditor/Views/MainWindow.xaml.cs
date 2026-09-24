using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RibbonXmlEditor.ViewModels;

namespace RibbonXmlEditor.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.RequestClose -= OnRequestClose;
        _vm = e.NewValue as MainViewModel;
        if (_vm is not null)
            _vm.RequestClose += OnRequestClose;
    }

    private void OnRequestClose(object? sender, EventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_vm is not null && !_vm.CanClose())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>Middle-click on a document tab header closes that tab.</summary>
    private void TabItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is TabItem { DataContext: EditorViewModel tab })
        {
            e.Handled = true;
            _vm?.CloseTab(tab);
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        foreach (var f in files.Where(f => f.EndsWith(".ribbon", StringComparison.OrdinalIgnoreCase)))
            _vm?.OpenFile(f);
    }
}
