using System.Text.RegularExpressions;
using RibbonXmlEditor.Schema;

namespace RibbonXmlEditor.Models;

/// <summary>An in-memory .ribbon file: leading comments, version stamp and the single root tab.</summary>
public sealed partial class RibbonDocument
{
    /// <summary>Matches the production version comment, e.g. <c> Version 2.2.0 2026-07-14 </c>.</summary>
    [GeneratedRegex(@"^\s*Version\s+(\d+(?:\.\d+)*)\s+(\d{4}-\d{2}-\d{2})\s*$", RegexOptions.CultureInvariant)]
    public static partial Regex VersionCommentRegex();

    public string? FilePath { get; set; }

    public RibbonNode Tab { get; set; } = new(ElementKind.Tab);

    /// <summary>Comments before the root element, excluding the version comment. Raw text, no delimiters.</summary>
    public List<string> OtherLeadingComments { get; } = new();

    /// <summary>Index among <see cref="OtherLeadingComments"/> at which the version comment is emitted. Past the end = last.</summary>
    public int VersionCommentPosition { get; set; } = int.MaxValue;

    public string Version { get; set; } = string.Empty;

    public DateOnly? VersionDate { get; set; }

    public bool HasVersionComment => Version.Length > 0 || VersionDate is not null;

    public string VersionCommentText
    {
        get
        {
            var date = VersionDate?.ToString("yyyy-MM-dd") ?? string.Empty;
            return $" Version {Version} {date} ".Replace("  ", " ");
        }
    }

    public static RibbonDocument CreateBlank(string tabName = "New Tab")
    {
        var doc = new RibbonDocument
        {
            Version = "1.0.0",
            VersionDate = DateOnly.FromDateTime(DateTime.Today),
        };
        doc.Tab["name"] = tabName;
        var panel = new RibbonNode(ElementKind.Panel);
        panel["name"] = tabName;
        var stack = new RibbonNode(ElementKind.StackedItems);
        var button = new RibbonNode(ElementKind.Button);
        button["name"] = "Button1";
        button["text"] = "Button 1";
        stack.AddChild(button);
        panel.AddChild(stack);
        doc.Tab.AddChild(panel);
        return doc;
    }
}
