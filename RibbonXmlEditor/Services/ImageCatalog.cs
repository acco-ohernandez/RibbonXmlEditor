using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace RibbonXmlEditor.Services;

/// <summary>Image helpers: non-locking thumbnails and the ACCO Name_16x16 / _32x32 / _192x192 sibling convention.</summary>
public static partial class ImageCatalog
{
    [GeneratedRegex(@"^(?<stem>.*?)_(?<size>16x16|32x32|64x64|192x192|16|32|192)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizedNameRegex();

    /// <summary>Loads a small, frozen bitmap without keeping the file open. Returns null for missing or unreadable files.</summary>
    public static BitmapSource? LoadThumbnail(string path, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try
        {
            if (!File.Exists(path))
                return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.DecodePixelWidth = decodePixelWidth;
            bmp.StreamSource = fs;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Given a picked image and the attribute it will be assigned to, returns the sibling file that the
    /// other image attribute conventionally uses (e.g. picked Name_16x16.png, attribute largeimage → Name_32x32.png),
    /// or null when the naming convention does not apply or the sibling does not exist.
    /// </summary>
    public static string? SiblingFor(string pickedPath, string targetAttribute)
    {
        string? wantedSize = targetAttribute switch
        {
            "image" => "16x16",
            "largeimage" => "32x32",
            "tooltipimage" => "192x192",
            _ => null,
        };
        if (wantedSize is null)
            return null;

        string dir;
        string stem;
        string ext;
        try
        {
            dir = Path.GetDirectoryName(pickedPath) ?? string.Empty;
            ext = Path.GetExtension(pickedPath);
            var m = SizedNameRegex().Match(Path.GetFileNameWithoutExtension(pickedPath));
            if (!m.Success)
                return null;
            stem = m.Groups["stem"].Value;
        }
        catch
        {
            return null;
        }

        // Try the full "_32x32" form first, then the short "_32" form some files use.
        foreach (var suffix in new[] { wantedSize, wantedSize.Split('x')[0] })
        {
            var candidate = Path.Combine(dir, $"{stem}_{suffix}{ext}");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
