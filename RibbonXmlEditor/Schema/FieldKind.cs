namespace RibbonXmlEditor.Schema;

/// <summary>How an attribute is edited in the property panel and validated.</summary>
public enum FieldKind
{
    Text,
    MultilineText,
    ImagePath,
    ClassName,
    Url,
    TrueOrEmpty,

    /// <summary>A GUID (any format Guid.TryParse accepts); the editor offers a "New" button.</summary>
    Guid,

    /// <summary>
    /// Full name of an IDockablePaneProvider class. Unlike <see cref="ClassName"/> it is not checked against the
    /// tab DLL's command list: the class usually lives in the Resources DLL and implements a different interface.
    /// </summary>
    PaneClassName,
}
