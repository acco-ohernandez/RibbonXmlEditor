namespace RibbonXmlEditor.Services;

/// <summary>
/// Keeps timestamped copies of files before they are overwritten. Backups live under
/// %LocalAppData% rather than next to the ribbon so deployed folders stay clean.
/// </summary>
public static class BackupService
{
    public static string BackupRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RibbonXmlEditor", "Backups");

    /// <summary>Copies <paramref name="path"/> to the backup folder if it exists. Returns the backup path or null.</summary>
    public static string? BackupIfExists(string path, int keepPerFile = 20)
    {
        if (!File.Exists(path))
            return null;

        Directory.CreateDirectory(BackupRoot);
        var name = Path.GetFileName(path);
        var dest = Path.Combine(BackupRoot, $"{name}.{DateTime.Now:yyyyMMdd-HHmmss}.bak");
        File.Copy(path, dest, overwrite: true);

        var stale = Directory.EnumerateFiles(BackupRoot, $"{name}.*.bak")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .Skip(keepPerFile);
        foreach (var f in stale)
        {
            try { File.Delete(f); } catch { /* best effort */ }
        }
        return dest;
    }
}
