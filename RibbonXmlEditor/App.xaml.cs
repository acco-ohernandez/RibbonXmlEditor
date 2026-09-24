using System.Windows;
using RibbonXmlEditor.ViewModels;
using RibbonXmlEditor.Views;

namespace RibbonXmlEditor;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Unexpected error:\n\n{args.Exception.Message}", "Ribbon XML Editor",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();

        // "Open with" / drag onto the exe: every existing path becomes a tab.
        foreach (var file in e.Args.Where(File.Exists))
            vm.OpenFile(file);
    }
}
