using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Services;

namespace RibbonXmlEditor.ViewModels;

/// <summary>The window shell: the list of open ribbon files (tabs), the active one, and file-level commands.</summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AppSettings _settings = AppSettings.Current;
    private EditorViewModel? _activeDocument;

    public MainViewModel()
    {
        NewCommand = new RelayCommand(NewBlank);
        NewFromTemplateCommand = new RelayCommand(NewFromTemplate);
        OpenCommand = new RelayCommand(Open);
        OpenRecentCommand = new RelayCommand<string>(OpenFile);
        OpenBackupsFolderCommand = new RelayCommand(OpenBackupsFolder);
        SetImagesFolderCommand = new RelayCommand(SetImagesFolder);
        AboutCommand = new RelayCommand(About);
        DocumentationCommand = new RelayCommand(() => Dialogs.ShowDocumentation());
        ExitCommand = new RelayCommand(() => RequestClose?.Invoke(this, EventArgs.Empty));

        SaveCommand = new RelayCommand(() => ActiveDocument?.Save(), () => ActiveDocument is not null);
        SaveAsCommand = new RelayCommand(() => ActiveDocument?.SaveAs(), () => ActiveDocument is not null);
        SaveAllCommand = new RelayCommand(SaveAll, () => HasDocuments);
        SelectDllCommand = new RelayCommand(() => ActiveDocument?.SelectDllCommand.Execute(null), () => ActiveDocument is not null);
        RescanDllCommand = new RelayCommand(() => ActiveDocument?.RescanDllCommand.Execute(null), () => ActiveDocument?.HasDll == true);
        CloseActiveCommand = new RelayCommand(() => { if (ActiveDocument is { } d) CloseTab(d); }, () => ActiveDocument is not null);
        CloseTabCommand = new RelayCommand<EditorViewModel>(tab => CloseTab(tab));
        CloseOthersCommand = new RelayCommand<EditorViewModel>(CloseOthers);
        CloseAllCommand = new RelayCommand(() => CloseAll(), () => HasDocuments);

        Documents.CollectionChanged += OnDocumentsChanged;
        RecentFiles = new ObservableCollection<string>(_settings.RecentFiles);
    }

    // ---- state -----------------------------------------------------------------------

    public ObservableCollection<EditorViewModel> Documents { get; } = new();

    public EditorViewModel? ActiveDocument
    {
        get => _activeDocument;
        set
        {
            if (ReferenceEquals(_activeDocument, value))
                return;
            _activeDocument?.Document.ClosePreviewPopups();
            _activeDocument = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            RaiseActiveCommands();
        }
    }

    public bool HasDocuments => Documents.Count > 0;

    public ObservableCollection<string> RecentFiles { get; }

    public string ImagesFolder => _settings.EffectiveImagesFolder;

    public string Title => ActiveDocument is null ? "Ribbon XML Editor" : $"{ActiveDocument.Header} - Ribbon XML Editor";

    /// <summary>Asks the window to close (after the usual save prompts).</summary>
    public event EventHandler? RequestClose;

    // ---- commands --------------------------------------------------------------------

    public RelayCommand NewCommand { get; }
    public RelayCommand NewFromTemplateCommand { get; }
    public RelayCommand OpenCommand { get; }
    public RelayCommand<string> OpenRecentCommand { get; }
    public RelayCommand OpenBackupsFolderCommand { get; }
    public RelayCommand SetImagesFolderCommand { get; }
    public RelayCommand AboutCommand { get; }
    public RelayCommand DocumentationCommand { get; }
    public RelayCommand ExitCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand SaveAsCommand { get; }
    public RelayCommand SaveAllCommand { get; }
    public RelayCommand SelectDllCommand { get; }
    public RelayCommand RescanDllCommand { get; }
    public RelayCommand CloseActiveCommand { get; }
    public RelayCommand<EditorViewModel> CloseTabCommand { get; }
    public RelayCommand<EditorViewModel> CloseOthersCommand { get; }
    public RelayCommand CloseAllCommand { get; }

    // ---- opening ---------------------------------------------------------------------

    private void NewBlank()
        => AddEditor(new EditorViewModel(this, RibbonDocument.CreateBlank(), Array.Empty<Issue>(), needsRepair: false));

    private void NewFromTemplate()
    {
        try
        {
            var result = TemplateProvider.LoadButtonStructureTemplate();
            result.Document.VersionDate = DateOnly.FromDateTime(DateTime.Today);
            AddEditor(new EditorViewModel(this, result.Document, result.Issues, result.NeedsRepairSave));
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Could not load the built-in template.\n\n{ex.Message}");
        }
    }

    private void Open()
    {
        var initial = ActiveDocument?.Document.FilePath is { } p ? Path.GetDirectoryName(p) : _settings.LastOpenFolder;
        var path = Dialogs.OpenRibbon(initial);
        if (path is not null)
            OpenFile(path);
    }

    /// <summary>Opens a file in a new tab, or activates the tab that already has it.</summary>
    public void OpenFile(string path)
    {
        var full = Path.GetFullPath(path);
        var existing = Documents.FirstOrDefault(d => string.Equals(d.Document.FilePath, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            ActiveDocument = existing;
            return;
        }

        try
        {
            var result = RibbonXmlReader.Load(full);
            AddEditor(new EditorViewModel(this, result.Document, result.Issues, result.NeedsRepairSave));
            RememberFile(full);
        }
        catch (Exception ex)
        {
            Dialogs.Error($"Could not open\n{full}\n\n{ex.Message}");
            _settings.RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            _settings.Save();
            RefreshRecent();
        }
    }

    private void AddEditor(EditorViewModel editor)
    {
        editor.PropertyChanged += OnEditorPropertyChanged;
        Documents.Add(editor);
        ActiveDocument = editor;
    }

    // ---- closing ---------------------------------------------------------------------

    /// <summary>Closes one tab (prompting to save when dirty). Returns false when the user cancels.</summary>
    public bool CloseTab(EditorViewModel tab)
    {
        if (!Documents.Contains(tab))
            return true;

        if (tab.Document.IsDirty)
        {
            ActiveDocument = tab; // show what we are asking about
            if (!tab.ConfirmDiscardChanges())
                return false;
        }

        if (ReferenceEquals(ActiveDocument, tab))
        {
            // Pick the neighbour BEFORE removing: otherwise the TabControl auto-selects and fights this choice.
            int i = Documents.IndexOf(tab);
            ActiveDocument = Documents.Count == 1 ? null : Documents[i > 0 ? i - 1 : 1];
        }

        tab.PropertyChanged -= OnEditorPropertyChanged;
        Documents.Remove(tab);
        tab.Detach();
        return true;
    }

    public void CloseOthers(EditorViewModel keep)
    {
        foreach (var d in Documents.Where(d => !ReferenceEquals(d, keep)).ToList())
        {
            if (!CloseTab(d))
                return;
        }
    }

    /// <summary>Closes every tab; returns false when the user cancels on a dirty one.</summary>
    public bool CloseAll()
    {
        foreach (var d in Documents.ToList())
        {
            if (!CloseTab(d))
                return false;
        }
        return true;
    }

    /// <summary>Window-close check: prompts for every dirty tab; false when the user cancels.</summary>
    public bool CanClose()
    {
        foreach (var d in Documents.ToList())
        {
            if (!d.Document.IsDirty)
                continue;
            ActiveDocument = d;
            if (!d.ConfirmDiscardChanges())
                return false;
        }
        return true;
    }

    // ---- saving ----------------------------------------------------------------------

    public void SaveAll()
    {
        foreach (var d in Documents.ToList())
        {
            if (!d.Document.IsDirty)
                continue;
            ActiveDocument = d;
            if (!d.Save())
                return;
        }
    }

    // ---- shared services used by editors ----------------------------------------------

    internal void RememberFile(string path)
    {
        _settings.AddRecent(path);
        _settings.LastOpenFolder = Path.GetDirectoryName(path);
        _settings.Save();
        RefreshRecent();
    }

    internal bool IsOpenElsewhere(string path, EditorViewModel except)
    {
        var full = Path.GetFullPath(path);
        return Documents.Any(d => !ReferenceEquals(d, except)
                                  && string.Equals(d.Document.FilePath, full, StringComparison.OrdinalIgnoreCase));
    }

    public void RevalidateAll()
    {
        foreach (var d in Documents)
            d.Revalidate();
    }

    // ---- misc commands ---------------------------------------------------------------

    private void RefreshRecent()
    {
        RecentFiles.Clear();
        foreach (var f in _settings.RecentFiles)
            RecentFiles.Add(f);
    }

    private void OpenBackupsFolder()
    {
        try
        {
            Directory.CreateDirectory(BackupService.BackupRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupService.BackupRoot}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Dialogs.Error(ex.Message);
        }
    }

    private void SetImagesFolder()
    {
        var picked = Dialogs.PickFolder(Directory.Exists(ImagesFolder) ? ImagesFolder : null, "Select the ribbon Images folder");
        if (picked is null)
            return;
        _settings.ImagesFolder = picked;
        _settings.Save();
        OnPropertyChanged(nameof(ImagesFolder));
        RevalidateAll();
    }

    private void About()
    {
        var version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "?";
        Dialogs.Info(
            $"Ribbon XML Editor {version}\n\n" +
            "Edits the .ribbon files that define ACCO Revit ribbon tabs.\n\n" +
            "The file format is defined by RibbonBuilder.cs in the BTT_ACCORevit-Ribbons solution " +
            "(RevitRibbon_MainSourceCode\\Ribbon Builder). If that parser changes, update RibbonSchema.cs in this tool.\n\n" +
            $"Backups: {BackupService.BackupRoot}\nSettings: {AppSettings.SettingsPath}");
    }

    // ---- plumbing --------------------------------------------------------------------

    private void OnDocumentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasDocuments));
        SaveAllCommand.RaiseCanExecuteChanged();
        CloseAllCommand.RaiseCanExecuteChanged();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, ActiveDocument))
            return;
        if (e.PropertyName is nameof(EditorViewModel.Header))
            OnPropertyChanged(nameof(Title));
        if (e.PropertyName is nameof(EditorViewModel.HasDll) or nameof(EditorViewModel.DllStatusText))
            RescanDllCommand.RaiseCanExecuteChanged();
    }

    private void RaiseActiveCommands()
    {
        SaveCommand.RaiseCanExecuteChanged();
        SaveAsCommand.RaiseCanExecuteChanged();
        SelectDllCommand.RaiseCanExecuteChanged();
        RescanDllCommand.RaiseCanExecuteChanged();
        CloseActiveCommand.RaiseCanExecuteChanged();
    }
}
