using System.Text.Json;

namespace RibbonXmlEditor.Services;

/// <summary>Per-user settings stored as JSON under %LocalAppData%\RibbonXmlEditor.</summary>
public sealed class AppSettings
{
    public const string DefaultImagesFolder = @"C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\Images";
    private const int MaxRecent = 10;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RibbonXmlEditor", "settings.json");

    public static AppSettings Current { get; } = Load();

    public List<string> RecentFiles { get; set; } = new();

    /// <summary>Tab DLL chosen for a given .ribbon path (keys are full paths).</summary>
    public Dictionary<string, string> DllByRibbon { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public string? ImagesFolder { get; set; }

    public string? LastOpenFolder { get; set; }

    public string EffectiveImagesFolder => string.IsNullOrWhiteSpace(ImagesFolder) ? DefaultImagesFolder : ImagesFolder;

    public void AddRecent(string path)
    {
        var full = Path.GetFullPath(path);
        RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, full);
        if (RecentFiles.Count > MaxRecent)
            RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Settings are a convenience; never fail the app over them.
        }
    }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (loaded is not null)
                {
                    loaded.DllByRibbon = new Dictionary<string, string>(loaded.DllByRibbon, StringComparer.OrdinalIgnoreCase);
                    return loaded;
                }
            }
        }
        catch
        {
            // fall through to defaults
        }
        return new AppSettings();
    }
}
