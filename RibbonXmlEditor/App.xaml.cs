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

        // "Open with" / drag onto the exe.
        var file = e.Args.FirstOrDefault(File.Exists);
        if (file is not null)
            vm.OpenFile(file);
    }
}
