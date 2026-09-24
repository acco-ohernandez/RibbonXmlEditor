using System.Text.RegularExpressions;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;

namespace RibbonXmlEditor.Services;

/// <param name="KnownClasses">Command classes found in the tab DLL, or null when no DLL is loaded.</param>
/// <param name="DllName">File name of the scanned DLL, for messages.</param>
/// <param name="ImagesFolder">The ribbon Images folder; when it does not exist on this machine, missing-image errors become warnings.</param>
public sealed record ValidationContext(IReadOnlySet<string>? KnownClasses, string? DllName, string ImagesFolder)
{
    public static ValidationContext Default { get; } = new(null, null, AppSettings.DefaultImagesFolder);
}

/// <summary>
/// Checks a document against what Revit's RibbonBuilder will actually do with it.
/// Errors = Revit throws, crashes, or silently truncates the ribbon. Warnings = works but suspicious.
/// </summary>
public static partial class RibbonValidator
{
    [GeneratedRegex(@"^[A-Za-z_]\w*([.+][A-Za-z_]\w*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex ClassNameRegex();

    public static List<Issue> Validate(RibbonDocument doc, ValidationContext? context = null)
    {
        context ??= ValidationContext.Default;
        var issues = new List<Issue>();
        bool imagesFolderMissing = !Directory.Exists(context.ImagesFolder);

        ValidateNode(doc.Tab, issues, context, imagesFolderMissing);
        return issues;
    }

    /// <summary>Save-time check: Revit loads only the first *.ribbon in a folder.</summary>
    public static IEnumerable<Issue> CheckSaveTarget(string path)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (dir is null || !Directory.Exists(dir))
            yield break;

        var others = Directory.GetFiles(dir, "*.ribbon")
            .Where(f => !string.Equals(f, full, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();
        if (others.Count > 0)
        {
            yield return new Issue(IssueSeverity.Warning, "save.multiple-ribbons",
                $"The folder already contains {string.Join(", ", others)}. Revit loads only the first *.ribbon file it finds in a folder, so one of them will be ignored.");
        }
    }

    // ----------------------------------------------------------------------------------

    private static void ValidateNode(RibbonNode node, List<Issue> issues, ValidationContext ctx, bool imagesFolderMissing)
    {
        var def = node.Def;

        foreach (var attr in def.Attributes)
        {
            var value = node[attr.XmlName];

            if (attr.IsRequired && value.Trim().Length == 0)
            {
                issues.Add(new Issue(IssueSeverity.Error, "required",
                    $"'{attr.Label}' is required. Revit shows an error and then fails while creating this {def.DisplayName.ToLowerInvariant()}.",
                    node, attr.XmlName));
                continue;
            }
            if (value.Length == 0)
                continue;

            switch (attr.Kind)
            {
                case FieldKind.ImagePath:
                    ValidateImage(node, attr, value, issues, ctx, imagesFolderMissing);
                    break;

                case FieldKind.Url:
                    if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                        || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    {
                        issues.Add(new Issue(IssueSeverity.Error, "help.url",
                            "Help URL must be an absolute http:// or https:// address. Revit throws when it builds the contextual help.",
                            node, attr.XmlName));
                    }
                    break;

                case FieldKind.ClassName:
                    if (!ClassNameRegex().IsMatch(value))
                    {
                        issues.Add(new Issue(IssueSeverity.Warning, "classname.format",
                            "Class name should look like Namespace.SubNamespace.ClassName (letters, digits, underscores and dots).",
                            node, attr.XmlName));
                    }
                    else if (ctx.KnownClasses is not null && !ctx.KnownClasses.Contains(value))
                    {
                        issues.Add(new Issue(IssueSeverity.Warning, "classname.unknown",
                            $"Class was not found in {ctx.DllName}. The button will appear in Revit but fail when clicked.",
                            node, attr.XmlName));
                    }
                    break;

                case FieldKind.TrueOrEmpty:
                    if (value != "true")
                    {
                        issues.Add(new Issue(IssueSeverity.Warning, "flag.value",
                            $"'{attr.Label}' must be exactly \"true\" or empty; Revit treats \"{value}\" as false.",
                            node, attr.XmlName));
                    }
                    break;
            }
        }

        // ---- children --------------------------------------------------------------------

        int count = node.Children.Count;

        foreach (var child in node.Children)
        {
            if (!def.Allows(child.Kind))
            {
                var allowed = def.AllowedChildren.Count == 0
                    ? "nothing"
                    : string.Join(", ", def.AllowedChildren.Select(k => RibbonSchema.ByKind(k).XmlName));
                issues.Add(new Issue(IssueSeverity.Error, "child.notallowed",
                    $"<{child.Def.XmlName}> is not allowed inside <{def.XmlName}> (allowed: {allowed}). Revit would crash reading it. Move or delete it.",
                    child));
            }
        }

        if (node.Kind == ElementKind.StackedItems && (count < 1 || count > 3))
        {
            var panel = node.Closest(ElementKind.Panel)?.Name ?? "?";
            issues.Add(new Issue(IssueSeverity.Error, "stacked.count",
                $"Stacked items must contain 1 to 3 items (this one has {count}). Revit stops reading the rest of panel \"{panel}\" at this element.",
                node));
        }
        else if (def.MinChildren > 0 && count == 0)
        {
            issues.Add(new Issue(IssueSeverity.Warning, "container.empty",
                $"This {def.DisplayName.ToLowerInvariant()} has no items and will appear empty in Revit.", node));
        }

        switch (node.Kind)
        {
            case ElementKind.Tab:
                ReportDuplicates(node.Children.Where(c => c.Kind == ElementKind.Panel), issues,
                    n => $"Panel name \"{n}\" is used more than once in this tab. Revit throws on duplicate panel names.");
                break;

            case ElementKind.Panel:
                ValidatePanel(node, issues);
                break;

            case ElementKind.PulldownButtons or ElementKind.ComboBox or ElementKind.RadioButtons:
                ReportDuplicates(node.Children, issues,
                    n => $"Name \"{n}\" is used more than once inside this {def.DisplayName.ToLowerInvariant()}. Revit throws on duplicate names.");
                break;
        }

        foreach (var child in node.Children)
            ValidateNode(child, issues, ctx, imagesFolderMissing);
    }

    private static void ValidatePanel(RibbonNode panel, List<Issue> issues)
    {
        var splits = panel.Children.Where(c => c.Kind == ElementKind.SplitButtons).Skip(1);
        foreach (var extra in splits)
        {
            issues.Add(new Issue(IssueSeverity.Error, "split.multiple",
                "Only one split button is possible per panel: Revit's RibbonBuilder gives every split button the same internal name, and a second one throws a duplicate-name exception.",
                extra));
        }

        // Every item that becomes a RibbonItem in this RibbonPanel must have a unique name.
        var panelItems = new List<RibbonNode>();
        foreach (var structure in panel.Children)
        {
            switch (structure.Kind)
            {
                case ElementKind.StackedItems:
                    panelItems.AddRange(structure.Children.Where(c => RibbonSchema.IsPanelItem(c.Kind)));
                    break;
                case ElementKind.RadioButtons:
                    panelItems.Add(structure);
                    break;
                case ElementKind.SplitButtons or ElementKind.SlideoutPanel:
                    panelItems.AddRange(structure.Children.Where(c => c.Kind == ElementKind.Button));
                    break;
            }
        }
        ReportDuplicates(panelItems, issues,
            n => $"Name \"{n}\" is used by more than one item in panel \"{panel.Name}\". Revit throws on duplicate item names within a panel.");
    }

    private static void ReportDuplicates(IEnumerable<RibbonNode> nodes, List<Issue> issues, Func<string, string> message)
    {
        foreach (var group in nodes.Where(n => n.Def.HasAttribute("name") && n.Name.Trim().Length > 0)
                                   .GroupBy(n => n.Name, StringComparer.Ordinal)
                                   .Where(g => g.Count() > 1))
        {
            foreach (var n in group)
                issues.Add(new Issue(IssueSeverity.Error, "name.duplicate", message(group.Key), n, "name"));
        }
    }

    private static void ValidateImage(RibbonNode node, AttributeDef attr, string value, List<Issue> issues,
        ValidationContext ctx, bool imagesFolderMissing)
    {
        bool fullyQualified;
        try { fullyQualified = Path.IsPathFullyQualified(value); }
        catch { fullyQualified = false; }

        if (!fullyQualified)
        {
            issues.Add(new Issue(IssueSeverity.Error, "image.relative",
                @"Image path must be absolute (for example C:\ACCORevit\...\Images\Name_16x16.png). Revit builds a Uri from it and a relative path throws at startup.",
                node, attr.XmlName));
            return;
        }

        if (!File.Exists(value))
        {
            bool underImagesFolder = value.StartsWith(ctx.ImagesFolder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            if (imagesFolderMissing && underImagesFolder)
            {
                issues.Add(new Issue(IssueSeverity.Warning, "image.unverified",
                    $"Image file cannot be checked on this computer (the Images folder {ctx.ImagesFolder} is not installed here).",
                    node, attr.XmlName));
            }
            else
            {
                issues.Add(new Issue(IssueSeverity.Error, "image.missing",
                    "Image file does not exist. Revit throws while loading the image and the tab fails to appear.",
                    node, attr.XmlName));
            }
        }

        // Size convention (ACCO naming: Name_16x16.png / _32x32 / _192x192).
        var file = Path.GetFileNameWithoutExtension(value).ToLowerInvariant();
        string? expected = null;
        string[] wrong = Array.Empty<string>();
        switch (attr.XmlName)
        {
            case "image" when node.Kind == ElementKind.PulldownButtons:
                expected = "32x32"; wrong = new[] { "_16x16", "_192x192" }; break;
            case "image":
                expected = "16x16"; wrong = new[] { "_32x32", "_192x192" }; break;
            case "largeimage":
                expected = "32x32"; wrong = new[] { "_16x16", "_192x192" }; break;
            case "tooltipimage":
                expected = "192x192"; wrong = new[] { "_16x16", "_32x32" }; break;
        }
        var hit = wrong.FirstOrDefault(w => file.Contains(w, StringComparison.Ordinal));
        if (hit is not null)
        {
            issues.Add(new Issue(IssueSeverity.Warning, "image.size",
                $"'{attr.Label}' is normally the {expected} image, but this file name says {hit.TrimStart('_')}.",
                node, attr.XmlName));
        }
    }
}
