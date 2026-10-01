using System.Globalization;
using System.Xml;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;

namespace RibbonXmlEditor.Services;

public sealed class RibbonReadResult
{
    public RibbonReadResult(RibbonDocument document, IReadOnlyList<Issue> issues, bool needsRepairSave)
    {
        Document = document;
        Issues = issues;
        NeedsRepairSave = needsRepairSave;
    }

    public RibbonDocument Document { get; }

    /// <summary>Load-time problems (rule ids start with <c>load.</c>).</summary>
    public IReadOnlyList<Issue> Issues { get; }

    /// <summary>True when the file differs from what the editor would write in a way that matters to Revit.</summary>
    public bool NeedsRepairSave { get; }
}

/// <summary>
/// Reads a .ribbon file the same way Revit's RibbonBuilder does: through <see cref="XmlDocument"/>.
/// That matters because <c>XmlDocument.Load</c> keeps raw line breaks inside attribute values
/// (production captions rely on them) while <c>XDocument</c> would collapse them to spaces.
/// </summary>
public static class RibbonXmlReader
{
    public static RibbonReadResult Load(string path)
    {
        var xml = new XmlDocument();
        xml.Load(path);
        var result = FromXmlDocument(xml);
        result.Document.FilePath = Path.GetFullPath(path);
        return result;
    }

    public static RibbonReadResult Parse(string xmlText)
    {
        var xml = new XmlDocument();
        xml.LoadXml(xmlText);
        return FromXmlDocument(xml);
    }

    public static RibbonReadResult FromXmlDocument(XmlDocument xml)
    {
        var doc = new RibbonDocument();
        var issues = new List<Issue>();
        bool repairs = false;

        var root = xml.DocumentElement
            ?? throw new InvalidDataException("The file has no root element.");

        bool afterRoot = false;
        foreach (XmlNode n in xml.ChildNodes)
        {
            if (ReferenceEquals(n, root))
            {
                afterRoot = true;
                continue;
            }
            if (n is not XmlComment c)
                continue;

            var text = c.Value ?? string.Empty;
            if (afterRoot)
            {
                issues.Add(new Issue(IssueSeverity.Warning, "load.trailing-comment",
                    $"A comment after </{root.Name}> was dropped: \"{text.Trim()}\"."));
                repairs = true;
                continue;
            }

            var m = RibbonDocument.VersionCommentRegex().Match(text);
            if (m.Success && doc.VersionCommentPosition == int.MaxValue)
            {
                doc.Version = m.Groups[1].Value;
                if (DateOnly.TryParseExact(m.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    doc.VersionDate = d;
                doc.VersionCommentPosition = doc.OtherLeadingComments.Count;
            }
            else
            {
                doc.OtherLeadingComments.Add(text);
            }
        }

        if (!string.Equals(root.Name, "tab", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The root element is <{root.Name}> but Revit's RibbonBuilder only reads a root <tab>. This is not a .ribbon file it can load.");
        }

        doc.Tab = ReadElement(root, RibbonSchema.ByKind(ElementKind.Tab), issues, ref repairs);
        return new RibbonReadResult(doc, issues, repairs);
    }

    private static RibbonNode ReadElement(XmlElement el, ElementDef def, List<Issue> issues, ref bool repairs)
    {
        var node = new RibbonNode(def.Kind);

        foreach (var a in def.Attributes)
        {
            var attr = el.Attributes[a.XmlName];
            if (attr is null)
            {
                repairs = true;
                var label = el.GetAttribute("name") is { Length: > 0 } nm ? $"<{def.XmlName} name=\"{nm}\">" : $"<{def.XmlName}>";
                issues.Add(new Issue(IssueSeverity.Warning, "load.missing-attribute",
                    $"{label} had no '{a.XmlName}' attribute. Revit crashes on a missing attribute; it will be written (empty) when you save.",
                    node, a.XmlName));
            }
            else
            {
                node[a.XmlName] = NormalizeNewlines(attr.Value);
            }
        }

        foreach (XmlAttribute attr in el.Attributes)
        {
            if (def.HasAttribute(attr.Name))
                continue;
            node.ExtraAttributes.Add(new KeyValuePair<string, string>(attr.Name, NormalizeNewlines(attr.Value)));
            issues.Add(new Issue(IssueSeverity.Warning, "load.unknown-attribute",
                $"<{def.XmlName}> has an attribute '{attr.Name}' that Revit does not read. It is kept as-is.", node, attr.Name));
        }

        foreach (XmlNode child in el.ChildNodes)
        {
            switch (child)
            {
                case XmlElement ce:
                    if (RibbonSchema.TryGetByXmlName(ce.Name, out var childDef))
                    {
                        // Disallowed-but-known children are kept so the validator can flag them and the user can move them.
                        node.AddChild(ReadElement(ce, childDef, issues, ref repairs));
                    }
                    else
                    {
                        repairs = true;
                        issues.Add(new Issue(IssueSeverity.Warning, "load.unknown-element",
                            $"<{ce.Name}> inside <{def.XmlName}> is not a ribbon element and was dropped.", node));
                    }
                    break;

                case XmlComment comment:
                    // A comment that holds exactly one ribbon element is a disabled item (see RibbonXmlWriter).
                    if (TryParseDisabledElement(comment.Value ?? string.Empty, out var disabledElement, out var disabledDef))
                    {
                        var disabled = ReadElement(disabledElement, disabledDef, issues, ref repairs);
                        disabled.IsDisabled = true;
                        node.AddChild(disabled);
                        break;
                    }
                    repairs = true;
                    issues.Add(new Issue(IssueSeverity.Warning, "load.comment-dropped",
                        $"A comment inside <{def.XmlName}> was dropped. Builders before 3.0 count comments as child items, which can break the 1-3 stacked-items limit.",
                        node));
                    break;

                case XmlText or XmlCDataSection:
                    if (!string.IsNullOrWhiteSpace(child.Value))
                    {
                        repairs = true;
                        issues.Add(new Issue(IssueSeverity.Warning, "load.text-dropped",
                            $"Loose text inside <{def.XmlName}> was dropped: \"{child.Value!.Trim()}\".", node));
                    }
                    break;
            }
        }

        return node;
    }

    /// <summary>Parses the text of a comment as XML; succeeds when it contains exactly one known ribbon element.</summary>
    private static bool TryParseDisabledElement(string commentText, out XmlElement element, out ElementDef def)
    {
        element = null!;
        def = null!;
        if (commentText.IndexOf('<') < 0)
            return false;
        try
        {
            var wrapper = new XmlDocument();
            wrapper.LoadXml("<x>" + commentText + "</x>");
            var elements = wrapper.DocumentElement!.ChildNodes.OfType<XmlElement>().ToList();
            if (elements.Count != 1 || !RibbonSchema.TryGetByXmlName(elements[0].Name, out var found))
                return false;
            element = elements[0];
            def = found;
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string NormalizeNewlines(string value)
        => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
}
