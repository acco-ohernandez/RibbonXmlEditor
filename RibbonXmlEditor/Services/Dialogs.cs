using System.Windows;
using Microsoft.Win32;

namespace RibbonXmlEditor.Services;

/// <summary>Thin wrappers over the standard Windows dialogs so view models stay free of WPF dialog code.</summary>
public static class Dialogs
{
    private const string Caption = "Ribbon XML Editor";
    private const string RibbonFilter = "Revit ribbon files (*.ribbon)|*.ribbon|All files (*.*)|*.*";

    private static Window? Owner => Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

    public static string? OpenRibbon(string? initialDir)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open ribbon file",
            Filter = RibbonFilter,
            CheckFileExists = true,
            InitialDirectory = ExistingDir(initialDir),
        };
        return Show(dlg) ? dlg.FileName : null;
    }

    public static string? SaveRibbon(string? initialDir, string? fileName)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save ribbon file",
            Filter = RibbonFilter,
            DefaultExt = ".ribbon",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = fileName ?? string.Empty,
            InitialDirectory = ExistingDir(initialDir),
        };
        return Show(dlg) ? dlg.FileName : null;
    }

    public static string? OpenDll(string? initialDir)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select the Revit tab DLL",
            Filter = "Revit tab DLL (*_Tab.dll)|*_Tab.dll|All DLLs (*.dll)|*.dll",
            CheckFileExists = true,
            InitialDirectory = ExistingDir(initialDir),
        };
        return Show(dlg) ? dlg.FileName : null;
    }

    public static string? OpenImage(string? initialDir)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select an image",
            Filter = "PNG images (*.png)|*.png|All images (*.png;*.bmp;*.jpg;*.ico)|*.png;*.bmp;*.jpg;*.jpeg;*.ico|All files (*.*)|*.*",
            CheckFileExists = true,
            InitialDirectory = ExistingDir(initialDir),
        };
        return Show(dlg) ? dlg.FileName : null;
    }

    public static string? PickFolder(string? initialDir, string title)
    {
        var dlg = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
            InitialDirectory = ExistingDir(initialDir),
        };
        return Show(dlg) ? dlg.FolderName : null;
    }

    public static bool Confirm(string message)
        => Show(message, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public static MessageBoxResult YesNoCancel(string message)
        => Show(message, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

    public static void Error(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Error);

    public static void Info(string message) => Show(message, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>Opens the built-in manual (non-modal, one instance), optionally at a topic.</summary>
    public static void ShowDocumentation(string? topic = null) => Views.DocumentationWindow.ShowOrActivate(Owner, topic);

    // ----------------------------------------------------------------------------------

    private static bool Show(CommonDialog dlg)
        => Owner is { } o ? dlg.ShowDialog(o) == true : dlg.ShowDialog() == true;

    private static MessageBoxResult Show(string message, MessageBoxButton buttons, MessageBoxImage image)
        => Owner is { } o
            ? MessageBox.Show(o, message, Caption, buttons, image)
            : MessageBox.Show(message, Caption, buttons, image);

    private static string ExistingDir(string? dir)
        => !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : string.Empty;
}
