using RibbonXmlEditor.Schema;

namespace RibbonXmlEditor.Models;

/// <summary>
/// One element of a .ribbon file. Generic on purpose: the element's attribute list and allowed
/// children come from <see cref="RibbonSchema"/>, so this class never has to know the format.
/// </summary>
public sealed class RibbonNode
{
    private readonly Dictionary<string, string> _attributes = new(StringComparer.Ordinal);
    private readonly List<RibbonNode> _children = new();

    public RibbonNode(ElementKind kind)
    {
        Kind = kind;
        Def = RibbonSchema.ByKind(kind);
        foreach (var a in Def.Attributes)
            _attributes[a.XmlName] = string.Empty;
    }

    public ElementKind Kind { get; }
    public ElementDef Def { get; }
    public RibbonNode? Parent { get; private set; }
    public IReadOnlyList<RibbonNode> Children => _children;

    /// <summary>Attributes present in the file that the schema does not know. Written back untouched.</summary>
    public List<KeyValuePair<string, string>> ExtraAttributes { get; } = new();

    /// <summary>Attribute value by XML name. Values use '\n' line breaks internally.</summary>
    public string this[string xmlName]
    {
        get => _attributes.TryGetValue(xmlName, out var v) ? v : string.Empty;
        set
        {
            if (!Def.HasAttribute(xmlName))
                throw new ArgumentException($"<{Def.XmlName}> has no '{xmlName}' attribute.", nameof(xmlName));
            _attributes[xmlName] = value ?? string.Empty;
        }
    }

    public string Name => this["name"];

    public int IndexInParent => Parent?._children.IndexOf(this) ?? -1;

    public void AddChild(RibbonNode child, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
            throw new InvalidOperationException("Node already has a parent. Remove it first.");
        child.Parent = this;
        if (index < 0 || index >= _children.Count)
            _children.Add(child);
        else
            _children.Insert(index, child);
    }

    public bool RemoveChild(RibbonNode child)
    {
        if (!_children.Remove(child))
            return false;
        child.Parent = null;
        return true;
    }

    /// <summary>Moves a child by <paramref name="delta"/> positions (negative = up). Returns false when at the edge.</summary>
    public bool MoveChild(RibbonNode child, int delta)
    {
        int from = _children.IndexOf(child);
        if (from < 0) return false;
        int to = from + delta;
        if (to < 0 || to >= _children.Count) return false;
        _children.RemoveAt(from);
        _children.Insert(to, child);
        return true;
    }

    public RibbonNode DeepClone()
    {
        var copy = new RibbonNode(Kind);
        foreach (var kv in _attributes)
            copy._attributes[kv.Key] = kv.Value;
        copy.ExtraAttributes.AddRange(ExtraAttributes);
        foreach (var child in _children)
            copy.AddChild(child.DeepClone());
        return copy;
    }

    public IEnumerable<RibbonNode> Ancestors()
    {
        for (var p = Parent; p is not null; p = p.Parent)
            yield return p;
    }

    public IEnumerable<RibbonNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var c in _children)
            foreach (var d in c.DescendantsAndSelf())
                yield return d;
    }

    public IEnumerable<RibbonNode> Descendants() => DescendantsAndSelf().Skip(1);

    /// <summary>Nearest ancestor (or self) of the given kind.</summary>
    public RibbonNode? Closest(ElementKind kind)
    {
        for (var n = this; n is not null; n = n.Parent)
            if (n.Kind == kind) return n;
        return null;
    }

    /// <summary>Short human label, e.g. <c>button "About"</c> or <c>stackeditems #2</c>.</summary>
    public string Describe()
    {
        var name = Name;
        if (name.Length > 0)
            return $"{Def.XmlName} \"{name}\"";
        int idx = IndexInParent;
        return idx >= 0 ? $"{Def.XmlName} #{idx + 1}" : Def.XmlName;
    }

    /// <summary>Path from the tab down to this node, e.g. <c>tab "ENG Mechanical" / panel "Tools" / stackeditems #2 / button "Parallel"</c>.</summary>
    public string PathText()
    {
        var parts = Ancestors().Reverse().Select(a => a.Describe()).ToList();
        parts.Add(Describe());
        return string.Join(" / ", parts);
    }

    public override string ToString() => Describe();
}
