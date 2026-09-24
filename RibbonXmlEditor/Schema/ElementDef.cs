namespace RibbonXmlEditor.Schema;

/// <summary>One element of the .ribbon format: its attributes and which children it accepts.</summary>
public sealed record ElementDef(
    ElementKind Kind,
    string XmlName,
    string DisplayName,
    string Description,
    IReadOnlyList<AttributeDef> Attributes,
    IReadOnlyList<ElementKind> AllowedChildren,
    int MinChildren,
    int MaxChildren)
{
    public bool CanHaveChildren => AllowedChildren.Count > 0;

    public bool HasAttribute(string xmlName) => GetAttribute(xmlName) is not null;

    public AttributeDef? GetAttribute(string xmlName)
    {
        foreach (var a in Attributes)
        {
            if (string.Equals(a.XmlName, xmlName, StringComparison.Ordinal))
                return a;
        }
        return null;
    }

    public bool Allows(ElementKind childKind) => AllowedChildren.Contains(childKind);
}
