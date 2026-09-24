namespace RibbonXmlEditor.Schema;

/// <summary>One XML attribute of a ribbon element.</summary>
/// <param name="XmlName">Attribute name exactly as RibbonBuilder reads it.</param>
/// <param name="Label">Label shown in the property panel.</param>
/// <param name="Kind">Editor / validation behaviour.</param>
/// <param name="IsRequired">True when RibbonBuilder shows an error and then throws if the value is empty.</param>
/// <param name="Hint">Help text shown under the field.</param>
public sealed record AttributeDef(string XmlName, string Label, FieldKind Kind, bool IsRequired, string Hint = "");
