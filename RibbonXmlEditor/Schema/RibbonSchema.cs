using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace RibbonXmlEditor.Schema;

/// <summary>
/// The single description of the .ribbon XML format understood by Revit's RibbonBuilder
/// (BTT_ACCORevit-Ribbons\RevitRibbon_MainSourceCode\Ribbon Builder\RibbonBuilder.cs).
/// Reader, writer, validator and property panel all derive from this table.
/// When the parser changes, change this table and nothing else.
/// </summary>
public static class RibbonSchema
{
    public const int Unbounded = int.MaxValue;

    // ---- attribute definitions shared by several elements -------------------------------

    private static AttributeDef Name(bool required = true, string hint = "Unique internal name Revit uses for this item.")
        => new("name", "Name", FieldKind.Text, required, hint);

    private static readonly AttributeDef ClassName = new("classname", "Command class", FieldKind.ClassName, true,
        "Full name of the IExternalCommand class inside the tab DLL, e.g. RevitRibbon_MainSourceCode.Cmd_About.");

    private static readonly AttributeDef Text = new("text", "Caption", FieldKind.MultilineText, true,
        "Text shown on the ribbon. Press Enter to break the caption onto a second line.");

    private static readonly AttributeDef Tooltip = new("tooltip", "Tooltip", FieldKind.MultilineText, false,
        "Shown when the mouse hovers over the item. Blank lines are allowed.");

    private static readonly AttributeDef LongDescription = new("longdescription", "Long description", FieldKind.MultilineText, false,
        "Extended tooltip text Revit shows after hovering a moment longer.");

    private static readonly AttributeDef Image = new("image", "Image (16x16)", FieldKind.ImagePath, false,
        "Absolute path to a 16x16 PNG. Used when the item is stacked or shown small.");

    private static readonly AttributeDef LargeImage = new("largeimage", "Large image (32x32)", FieldKind.ImagePath, false,
        "Absolute path to a 32x32 PNG. Used when the item is shown at full size.");

    private static readonly AttributeDef TooltipImage = new("tooltipimage", "Tooltip image (192x192)", FieldKind.ImagePath, false,
        "Absolute path to a PNG shown inside the tooltip.");

    private static readonly AttributeDef ContextHelp = new("contexthelp", "Help URL", FieldKind.Url, false,
        "Web page opened when the user presses F1 on the item. Must be an absolute http(s) URL.");

    // ---- the table -----------------------------------------------------------------------

    public static readonly IReadOnlyList<ElementDef> All = new ReadOnlyCollection<ElementDef>(new[]
    {
        new ElementDef(ElementKind.Tab, "tab", "Tab",
            "A ribbon tab. Each .ribbon file defines exactly one.",
            new[] { Name(hint: "Tab title shown in the Revit ribbon.") },
            new[] { ElementKind.Panel, ElementKind.DockablePane }, 0, Unbounded),

        new ElementDef(ElementKind.DockablePane, "dockablepane", "Dockable pane",
            "A dockable pane (like Revit's Project Browser) registered at startup from a class that implements " +
            "IDockablePaneProvider (builder 3.1+). It is not a ribbon item: it has no children, sits directly under the tab, " +
            "and is shown or hidden by a button command through RibbonBuilder.TryGetDockablePane(name). " +
            "Registering alone only adds the pane to Revit's View > User Interface list; a button whose command class " +
            "toggles it is the launcher (a panel's \"Add dockable toggle\" creates both). " +
            "A pane another add-in already registered under the same GUID is reused, not registered twice.",
            new[]
            {
                Name(hint: "Key that commands use to find the pane (RibbonBuilder.TryGetDockablePane). Case-insensitive, unique per file."),
                new AttributeDef("guid", "GUID", FieldKind.Guid, true,
                    "Identity of the pane in Revit (DockablePaneId). Any standard GUID format; keep it stable once deployed, " +
                    "because Revit remembers the pane's layout by it."),
                new AttributeDef("title", "Title", FieldKind.Text, false,
                    "Caption shown in Revit (pane header and View > User Interface list); defaults to the name."),
                new AttributeDef("classname", "Pane class", FieldKind.PaneClassName, true,
                    "Full name of a public class implementing IDockablePaneProvider with a constructor taking " +
                    "UIControlledApplication or no parameters. May live in a referenced DLL such as RevitRibbon_MainSourceCode_Resources.dll."),
                new AttributeDef("startshidden", "Start hidden", FieldKind.TrueOrEmpty, false,
                    "Hide the pane on the first view activation of the session (Revit shows a newly registered pane); " +
                    "the ribbon button then shows it on demand."),
            },
            Array.Empty<ElementKind>(), 0, 0),

        new ElementDef(ElementKind.Panel, "panel", "Panel",
            "A titled group of items inside the tab.",
            new[] { Name(hint: "Panel title shown under the items.") },
            new[] { ElementKind.Separator, ElementKind.StackedItems, ElementKind.SplitButtons, ElementKind.SlideoutPanel, ElementKind.RadioButtons },
            0, Unbounded),

        new ElementDef(ElementKind.Separator, "separator", "Separator",
            "A vertical divider line between items.",
            Array.Empty<AttributeDef>(), Array.Empty<ElementKind>(), 0, 0),

        new ElementDef(ElementKind.StackedItems, "stackeditems", "Stacked items",
            "1 to 3 items. One item is shown large; 2 or 3 are stacked vertically at small size. " +
            "Revit skips a stack with 0 or more than 3 items (builders before 3.0 stop reading the rest of the panel).",
            Array.Empty<AttributeDef>(),
            new[] { ElementKind.Button, ElementKind.PulldownButtons, ElementKind.ComboBox, ElementKind.TextBox }, 1, 3),

        new ElementDef(ElementKind.SplitButtons, "splitbuttons", "Split button",
            "A large button whose lower half opens a list of alternative buttons. Only one per panel.",
            Array.Empty<AttributeDef>(), new[] { ElementKind.Button }, 1, Unbounded),

        new ElementDef(ElementKind.SlideoutPanel, "slideoutpanel", "Slide-out",
            "Buttons hidden behind the panel title; they appear when the title is clicked.",
            new[] { Name(required: false, hint: "Not used by Revit; kept for documentation.") },
            new[] { ElementKind.Button }, 1, Unbounded),

        new ElementDef(ElementKind.RadioButtons, "radiobuttons", "Radio group",
            "A group of toggle buttons where only one can be active.",
            new[] { Name() }, new[] { ElementKind.ToggleButton }, 1, Unbounded),

        new ElementDef(ElementKind.Button, "button", "Button",
            "A push button that runs a command class.",
            new[] { Name(), ClassName, Text, Tooltip, Image, LargeImage, TooltipImage, ContextHelp },
            Array.Empty<ElementKind>(), 0, 0),

        new ElementDef(ElementKind.PulldownButtons, "pulldownbuttons", "Pulldown",
            "A drop-down that lists several buttons.",
            new[]
            {
                Name(hint: "Text shown on the pulldown."),
                new AttributeDef("image", "Image (32x32)", FieldKind.ImagePath, true,
                    "Absolute path to a 32x32 PNG. Revit shows it as the pulldown's large image."),
            },
            new[] { ElementKind.Button }, 1, Unbounded),

        new ElementDef(ElementKind.ComboBox, "combobox", "Combo box",
            "A drop-down list of selectable members. Selection handling must be coded in the add-in.",
            new[]
            {
                Name(),
                new AttributeDef("itemtext", "Item text", FieldKind.MultilineText, false, "Text shown next to the combo box."),
                LongDescription, Tooltip, TooltipImage,
            },
            new[] { ElementKind.ComboBoxMember }, 1, Unbounded),

        new ElementDef(ElementKind.ComboBoxMember, "comboboxmember", "Combo member",
            "One entry of a combo box.",
            new[]
            {
                Name(), Text,
                new AttributeDef("image", "Image (16x16)", FieldKind.ImagePath, false, "Absolute path to a 16x16 PNG shown next to the entry."),
                new AttributeDef("groupname", "Group", FieldKind.Text, false, "Members with the same group are listed together under a header."),
            },
            Array.Empty<ElementKind>(), 0, 0),

        new ElementDef(ElementKind.TextBox, "textbox", "Text box",
            "A text entry field. Enter-key handling must be coded in the add-in.",
            new[]
            {
                Name(),
                new AttributeDef("prompttext", "Prompt text", FieldKind.MultilineText, false, "Grey placeholder text shown while the box is empty."),
                LongDescription,
                new AttributeDef("image", "Image (16x16)", FieldKind.ImagePath, false, "Absolute path to a 16x16 PNG shown in the box."),
                new AttributeDef("showimage", "Show image as button", FieldKind.TrueOrEmpty, false, "When checked, the image acts as a clickable button."),
                Tooltip, TooltipImage,
            },
            Array.Empty<ElementKind>(), 0, 0),

        new ElementDef(ElementKind.ToggleButton, "togglebutton", "Toggle button",
            "One option of a radio group.",
            new[] { Name(), Text, Tooltip, LargeImage },
            Array.Empty<ElementKind>(), 0, 0),
    });

    private static readonly Dictionary<ElementKind, ElementDef> ByKindMap = All.ToDictionary(d => d.Kind);
    private static readonly Dictionary<string, ElementDef> ByXmlNameMap = All.ToDictionary(d => d.XmlName, StringComparer.Ordinal);

    public static ElementDef ByKind(ElementKind kind) => ByKindMap[kind];

    public static bool TryGetByXmlName(string xmlName, [MaybeNullWhen(false)] out ElementDef def)
        => ByXmlNameMap.TryGetValue(xmlName, out def);

    /// <summary>Kinds that become a RibbonItem directly inside a RibbonPanel (their names must be unique per panel).</summary>
    public static bool IsPanelItem(ElementKind kind) => kind is ElementKind.Button
        or ElementKind.PulldownButtons or ElementKind.ComboBox or ElementKind.TextBox or ElementKind.RadioButtons;
}
