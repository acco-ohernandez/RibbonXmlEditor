using System.Text;
using System.Xml;
using RibbonXmlEditor.Models;

namespace RibbonXmlEditor.Services;

/// <summary>
/// Writes a <see cref="RibbonDocument"/> in the exact shape Revit's RibbonBuilder expects:
/// UTF-8 without BOM, CRLF, one attribute per line, every schema attribute always present,
/// and raw line breaks inside attribute values (matching the production files).
/// </summary>
public static class RibbonXmlWriter
{
    private static XmlWriterSettings CreateSettings() => new()
    {
        Indent = true,
        IndentChars = "  ",
        NewLineChars = "\r\n",
        NewLineOnAttributes = true,
        NewLineHandling = NewLineHandling.None,
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        CloseOutput = false,
    };

    public static byte[] ToBytes(RibbonDocument doc)
    {
        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, CreateSettings()))
        {
            Write(doc, w);
        }
        return ms.ToArray();
    }

    public static string ToXmlString(RibbonDocument doc) => Encoding.UTF8.GetString(ToBytes(doc));

    /// <summary>
    /// Saves atomically: writes to a temp file in the same folder, then moves it over the target,
    /// so a half-written .ribbon never sits next to the tab DLL.
    /// </summary>
    public static void Save(RibbonDocument doc, string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? throw new ArgumentException("Path has no directory.", nameof(path));
        Directory.CreateDirectory(dir);

        var bytes = ToBytes(doc);
        var temp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }
        doc.FilePath = full;
    }

    private static void Write(RibbonDocument doc, XmlWriter w)
    {
        w.WriteStartDocument();

        var others = doc.OtherLeadingComments;
        int pos = Math.Min(doc.VersionCommentPosition, others.Count);
        for (int i = 0; i < others.Count; i++)
        {
            if (i == pos && doc.HasVersionComment)
                w.WriteComment(SafeComment(doc.VersionCommentText));
            w.WriteComment(SafeComment(others[i]));
        }
        if (pos >= others.Count && doc.HasVersionComment)
            w.WriteComment(SafeComment(doc.VersionCommentText));

        WriteElement(doc.Tab, w);
        w.WriteEndDocument();
    }

    private static void WriteElement(RibbonNode node, XmlWriter w)
    {
        w.WriteStartElement(node.Def.XmlName);
        foreach (var a in node.Def.Attributes)
            w.WriteAttributeString(a.XmlName, ToFileNewlines(node[a.XmlName]));
        foreach (var extra in node.ExtraAttributes)
            w.WriteAttributeString(extra.Key, ToFileNewlines(extra.Value));
        foreach (var child in node.Children)
            WriteElement(child, w);
        w.WriteEndElement();
    }

    private static string ToFileNewlines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

    /// <summary>XML comments may not contain "--"; soften them instead of throwing on save.</summary>
    private static string SafeComment(string text)
        => text.Replace("--", "- -", StringComparison.Ordinal);
}
