// Version 3.1.0 2026-10-08
//
// RibbonBuilder — builds a Revit ribbon tab from a .ribbon XML file that sits next to the add-in DLL.
//
// Single-file drop-in for any Revit add-in project (Revit 2023–2027; net48, net8.0-windows, net10.0-windows).
// See README.md next to this file for the .ribbon format, usage and error behaviour.
//
// Call from IExternalApplication.OnStartup:
//     RibbonBuilder.BuildRibbon(application, Assembly.GetExecutingAssembly().Location,
//                               Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
//
// 3.1: a <dockablepane> element directly under <tab> registers a dockable pane during the same call
// (RegisterDockablePane is only legal in OnStartup, which is exactly when BuildRibbon runs):
//     <dockablepane name="MyPane" guid="..." title="My Pane"
//                   classname="Namespace.MyPaneUserControl" startshidden="true" />
// The class must implement IDockablePaneProvider and have a constructor taking UIControlledApplication
// (preferred) or no parameters. Commands find the pane by name: RibbonBuilder.TryGetDockablePane(name, out id).
//
// Only C# features available on .NET Framework 4.8 are used (no init accessors, records, ranges or
// collection expressions), and every using is explicit so projects without implicit usings compile too.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using System.Xml;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using ComboBox = Autodesk.Revit.UI.ComboBox;
using TextBox = Autodesk.Revit.UI.TextBox;

namespace RevitRibbon_MainSourceCode
{
    /// <summary>Optional settings for <see cref="RibbonBuilder.BuildRibbon"/>. Everything is optional.</summary>
    public sealed class RibbonBuildOptions
    {
        /// <summary>A specific .ribbon file (absolute, or relative to the ribbon folder). Default: the first *.ribbon in the folder.</summary>
        public string? RibbonFile { get; set; }

        /// <summary>Show one TaskDialog listing every problem found while building. Default true.</summary>
        public bool ShowProblemsDialog { get; set; } = true;

        /// <summary>Receives every problem message and the final summary (in addition to Debug output).</summary>
        public Action<string>? Log { get; set; }

        /// <summary>Attached to every combo box created from the file.</summary>
        public EventHandler<ComboBoxCurrentChangedEventArgs>? ComboBoxCurrentChanged { get; set; }

        /// <summary>Attached to every combo box created from the file.</summary>
        public EventHandler<ComboBoxDropDownOpenedEventArgs>? ComboBoxDropDownOpened { get; set; }

        /// <summary>Attached to every combo box created from the file.</summary>
        public EventHandler<ComboBoxDropDownClosedEventArgs>? ComboBoxDropDownClosed { get; set; }

        /// <summary>Attached to every text box created from the file.</summary>
        public EventHandler<TextBoxEnterPressedEventArgs>? TextBoxEnterPressed { get; set; }
    }

    /// <summary>
    /// Reads a .ribbon XML file and creates the matching Revit ribbon tab, panels and items.
    /// Invalid parts of the file are reported (one dialog at the end, plus Debug output) and skipped;
    /// a problem never aborts the rest of the ribbon.
    /// </summary>
    public static class RibbonBuilder
    {
        public const string Version = "3.1.0";

        /// <summary>
        /// Builds the ribbon described by the .ribbon file in <paramref name="ribbonFolder"/>.
        /// </summary>
        /// <param name="application">The UIControlledApplication passed to IExternalApplication.OnStartup.</param>
        /// <param name="assemblyPath">Full path of the DLL that contains the IExternalCommand classes named in the file.</param>
        /// <param name="ribbonFolder">Folder that contains the .ribbon file (normally the DLL's folder).</param>
        /// <param name="options">Optional settings and event handlers.</param>
        public static void BuildRibbon(UIControlledApplication application, string assemblyPath, string ribbonFolder, RibbonBuildOptions? options = null)
        {
            if (application == null)
                throw new ArgumentNullException(nameof(application));
            if (string.IsNullOrEmpty(assemblyPath))
                throw new ArgumentException("The add-in assembly path is required.", nameof(assemblyPath));
            if (string.IsNullOrEmpty(ribbonFolder))
                throw new ArgumentException("The ribbon folder is required.", nameof(ribbonFolder));

            new Session(application, assemblyPath, ribbonFolder, options ?? new RibbonBuildOptions()).Run();
        }

        /// <summary>Kept so IExternalApplication code written for version 2.x compiles unchanged.</summary>
        [Obsolete("Use BuildRibbon(application, assemblyPath, ribbonFolder).")]
        public static void build_ribbon(UIControlledApplication application, string input_assembly_file_path, string input_ribbon_file_path)
            => BuildRibbon(application, input_assembly_file_path, input_ribbon_file_path);

        /// <summary>
        /// Every element and attribute name of the .ribbon format. The RibbonXmlEditor keeps its own schema
        /// table in sync with these constants (a unit test compares them), so change them here first.
        /// </summary>
        public static class XmlNames
        {
            // Elements
            public const string Tab = "tab";
            public const string Panel = "panel";
            public const string Separator = "separator";
            public const string StackedItems = "stackeditems";
            public const string SplitButtons = "splitbuttons";
            public const string SlideoutPanel = "slideoutpanel";
            public const string RadioButtons = "radiobuttons";
            public const string Button = "button";
            public const string PulldownButtons = "pulldownbuttons";
            public const string ComboBox = "combobox";
            public const string ComboBoxMember = "comboboxmember";
            public const string TextBox = "textbox";
            public const string ToggleButton = "togglebutton";
            public const string DockablePane = "dockablepane";

            // Attributes
            public const string Name = "name";
            public const string Classname = "classname";
            public const string Guid = "guid";
            public const string Title = "title";
            public const string StartsHidden = "startshidden";
            public const string Text = "text";
            public const string Tooltip = "tooltip";
            public const string Image = "image";
            public const string LargeImage = "largeimage";
            public const string TooltipImage = "tooltipimage";
            public const string ContextHelp = "contexthelp";
            public const string ItemText = "itemtext";
            public const string LongDescription = "longdescription";
            public const string GroupName = "groupname";
            public const string PromptText = "prompttext";
            public const string ShowImage = "showimage";
        }

        // ---- dockable panes (3.1) ------------------------------------------------------------

        /// <summary>A dockable pane declared by a &lt;dockablepane&gt; element and handled by <see cref="BuildRibbon"/>.</summary>
        public sealed class DockablePaneRegistration
        {
            public DockablePaneRegistration(string name, DockablePaneId id, string title, object? provider)
            {
                Name = name;
                Id = id;
                Title = title;
                Provider = provider;
            }

            /// <summary>The element's name attribute; the key commands look the pane up by.</summary>
            public string Name { get; }

            /// <summary>The pane id built from the element's guid attribute. Pass it to UIApplication.GetDockablePane.</summary>
            public DockablePaneId Id { get; }

            public string Title { get; }

            /// <summary>
            /// The IDockablePaneProvider instance this add-in created and registered, or null when the pane
            /// was already registered by another add-in (same guid) and only its id was recorded.
            /// </summary>
            public object? Provider { get; }
        }

        // One table per add-in assembly (the shared project compiles into every tab DLL), filled during BuildRibbon.
        private static readonly Dictionary<string, DockablePaneRegistration> _dockablePanes =
            new Dictionary<string, DockablePaneRegistration>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The pane id for a &lt;dockablepane name="..."&gt; of this add-in's ribbon file. False when no such pane was built.</summary>
        public static bool TryGetDockablePane(string name, out DockablePaneId id)
        {
            DockablePaneRegistration? registration = GetDockablePane(name);
            id = registration == null ? null! : registration.Id;
            return registration != null;
        }

        /// <summary>Everything recorded for a &lt;dockablepane name="..."&gt;, or null.</summary>
        public static DockablePaneRegistration? GetDockablePane(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            DockablePaneRegistration? registration;
            lock (_dockablePanes)
                return _dockablePanes.TryGetValue(name, out registration) ? registration : null;
        }

        /// <summary>The provider (the pane's UserControl) this add-in created for the named pane, or null (see <see cref="DockablePaneRegistration.Provider"/>).</summary>
        public static object? GetDockablePaneProvider(string name) => GetDockablePane(name)?.Provider;

        private static bool TryAddDockablePane(DockablePaneRegistration registration)
        {
            lock (_dockablePanes)
            {
                if (_dockablePanes.ContainsKey(registration.Name))
                    return false;
                _dockablePanes.Add(registration.Name, registration);
                return true;
            }
        }

        /// <summary>One build run: holds the inputs and the list of problems so helpers need no parameter threading.</summary>
        private sealed class Session
        {
            // Internal names of pulldown and split buttons. Revit derives the command IDs of their child buttons
            // (used by keyboard shortcuts and Quick Access Toolbar pins) from these, so the first one in a panel
            // keeps the literal names earlier versions used; a second one gets a numeric suffix.
            private const string PulldownInternalName = "PullDown";
            private const string SplitInternalName = "SplitButton";
            private const int MaxDialogLines = 25;

            private readonly UIControlledApplication _app;
            private readonly string _assemblyPath;
            private readonly string _ribbonFolder;
            private readonly RibbonBuildOptions _options;
            private readonly List<string> _problems = new List<string>();

            // Panes declared with startshidden="true": hidden once, on the first ViewActivated of the session.
            private readonly List<DockablePaneId> _panesToHide = new List<DockablePaneId>();
            private bool _hideSubscribed;

            public Session(UIControlledApplication app, string assemblyPath, string ribbonFolder, RibbonBuildOptions options)
            {
                _app = app;
                _assemblyPath = assemblyPath;
                _ribbonFolder = ribbonFolder;
                _options = options;
            }

            public void Run()
            {
                string? file = ResolveRibbonFile();
                if (file == null)
                {
                    Finish("(no ribbon file)");
                    return;
                }

                // XmlDocument keeps raw line breaks inside attribute values (captions rely on them); do not switch to XDocument.
                XmlDocument document = new XmlDocument();
                try
                {
                    document.Load(file);
                }
                catch (Exception ex)
                {
                    Report("Cannot read the ribbon file: " + ex.Message);
                    Finish(file);
                    return;
                }

                XmlElement? root = document.DocumentElement;
                if (root == null || root.Name != XmlNames.Tab)
                {
                    Report("The root element must be <tab>; found <" + (root == null ? "nothing" : root.Name) + ">. Nothing was built.");
                    Finish(file);
                    return;
                }

                BuildTab(root);
                SubscribeStartupHide();
                Finish(file);
            }

            // ---- file ----------------------------------------------------------------------

            private string? ResolveRibbonFile()
            {
                if (!string.IsNullOrEmpty(_options.RibbonFile))
                {
                    string explicitPath = System.IO.Path.IsPathRooted(_options.RibbonFile)
                        ? _options.RibbonFile!
                        : System.IO.Path.Combine(_ribbonFolder, _options.RibbonFile!);
                    if (File.Exists(explicitPath))
                        return explicitPath;
                    Report("Ribbon file not found: " + explicitPath);
                    return null;
                }

                if (!Directory.Exists(_ribbonFolder))
                {
                    Report("Ribbon folder not found: " + _ribbonFolder);
                    return null;
                }

                string[] files = Directory.GetFiles(_ribbonFolder, "*.ribbon");
                if (files.Length == 0)
                {
                    Report("No *.ribbon file found in " + _ribbonFolder);
                    return null;
                }

                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                if (files.Length > 1)
                    Report(files.Length + " *.ribbon files found in " + _ribbonFolder + "; only " + System.IO.Path.GetFileName(files[0]) + " is used.");
                return files[0];
            }

            // ---- tab and panels -------------------------------------------------------------

            private void BuildTab(XmlElement tab)
            {
                string tabName = Attr(tab, XmlNames.Name);
                if (tabName.Trim().Length == 0)
                {
                    Report("<tab> has no name; nothing was built.");
                    return;
                }

                try
                {
                    _app.CreateRibbonTab(tabName);
                }
                catch (Exception ex)
                {
                    // Typically "tab already exists" (another add-in created it). Panels are still added to it below.
                    Report("Could not create tab \"" + tabName + "\" (" + ex.Message + "); trying to add the panels to the existing tab.");
                }

                foreach (XmlElement child in Elements(tab))
                {
                    switch (child.Name)
                    {
                        case XmlNames.Panel:
                            BuildPanel(tabName, child);
                            break;
                        case XmlNames.DockablePane:
                            BuildDockablePane(child);
                            break;
                        default:
                            Report(Describe(child) + " is not allowed directly under <tab> and was ignored.");
                            break;
                    }
                }
            }

            // ---- dockable panes (3.1) -------------------------------------------------------

            /// <summary>
            /// Registers the pane described by a &lt;dockablepane&gt; element. Required: name, guid, classname.
            /// Optional: title (defaults to name), startshidden="true" (the pane is hidden on the first view activation,
            /// because Revit shows a newly registered pane and the first document view would undo an earlier Hide).
            /// A pane that another add-in already registered under the same guid is recorded (so this add-in's
            /// commands can still show it) but not registered again.
            /// </summary>
            private void BuildDockablePane(XmlElement element)
            {
                if (!Require(element, XmlNames.Name, XmlNames.Guid, XmlNames.Classname))
                    return;

                string name = Attr(element, XmlNames.Name).Trim();
                Guid guid;
                if (!System.Guid.TryParse(Attr(element, XmlNames.Guid).Trim(), out guid))
                {
                    Report(Describe(element) + ": guid \"" + Attr(element, XmlNames.Guid) + "\" is not a valid GUID; the pane was skipped.");
                    return;
                }

                string title = Attr(element, XmlNames.Title).Trim();
                if (title.Length == 0)
                    title = name;
                bool startsHidden = string.Equals(Attr(element, XmlNames.StartsHidden).Trim(), "true", StringComparison.OrdinalIgnoreCase);

                DockablePaneId paneId = new DockablePaneId(guid);

                bool alreadyExists;
                try
                {
                    alreadyExists = DockablePane.PaneExists(paneId);
                }
                catch (Exception ex)
                {
                    Report(Describe(element) + ": could not query the pane state (" + ex.Message + "); the pane was skipped.");
                    return;
                }

                object? provider = null;
                if (alreadyExists)
                {
                    Debug.WriteLine("[RibbonBuilder] " + Describe(element) + " is already registered by another add-in; reusing it.");
                }
                else
                {
                    string className = Attr(element, XmlNames.Classname).Trim();
                    Type? type = ResolveType(className);
                    if (type == null)
                    {
                        Report(Describe(element) + ": class \"" + className + "\" was not found in " + System.IO.Path.GetFileName(_assemblyPath) + " or the assemblies it references; the pane was skipped.");
                        return;
                    }
                    if (!typeof(IDockablePaneProvider).IsAssignableFrom(type))
                    {
                        Report(Describe(element) + ": class \"" + className + "\" does not implement IDockablePaneProvider; the pane was skipped.");
                        return;
                    }

                    provider = CreateProvider(type, element);
                    if (provider == null)
                        return;

                    try
                    {
                        _app.RegisterDockablePane(paneId, title, (IDockablePaneProvider)provider);
                    }
                    catch (Exception ex)
                    {
                        Report(Describe(element) + ": RegisterDockablePane failed (" + ex.Message + "); the pane was skipped.");
                        return;
                    }
                    Debug.WriteLine("[RibbonBuilder] " + Describe(element) + " registered as \"" + title + "\".");
                }

                if (!TryAddDockablePane(new DockablePaneRegistration(name, paneId, title, provider)))
                {
                    Report(Describe(element) + ": a pane with this name was already built; the duplicate was ignored.");
                    return;
                }

                if (startsHidden)
                    _panesToHide.Add(paneId);
            }

            /// <summary>Instantiates the pane class: a constructor taking UIControlledApplication is preferred, else a parameterless one.</summary>
            private object? CreateProvider(Type type, XmlElement element)
            {
                ConstructorInfo? withApp = type.GetConstructor(new[] { typeof(UIControlledApplication) });
                ConstructorInfo? parameterless = withApp == null ? type.GetConstructor(Type.EmptyTypes) : null;
                if (withApp == null && parameterless == null)
                {
                    Report(Describe(element) + ": class \"" + type.FullName + "\" needs a public constructor taking UIControlledApplication or no parameters; the pane was skipped.");
                    return null;
                }

                try
                {
                    return withApp != null ? withApp.Invoke(new object[] { _app }) : parameterless!.Invoke(null);
                }
                catch (TargetInvocationException ex)
                {
                    Exception inner = ex.InnerException ?? ex;
                    Report(Describe(element) + ": the constructor of \"" + type.FullName + "\" threw " + inner.GetType().Name + ": " + inner.Message + "; the pane was skipped.");
                    return null;
                }
                catch (Exception ex)
                {
                    Report(Describe(element) + ": could not create \"" + type.FullName + "\" (" + ex.Message + "); the pane was skipped.");
                    return null;
                }
            }

            /// <summary>
            /// Finds a type by full name in the add-in assembly (this one), then in the assemblies it references
            /// (e.g. the Resources class library), then in anything else already loaded.
            /// </summary>
            private static Type? ResolveType(string fullName)
            {
                Assembly addin = typeof(RibbonBuilder).Assembly;
                Type? type = addin.GetType(fullName, false);
                if (type != null)
                    return type;

                foreach (AssemblyName reference in addin.GetReferencedAssemblies())
                {
                    try
                    {
                        type = Assembly.Load(reference).GetType(fullName, false);
                    }
                    catch (Exception)
                    {
                        type = null; // a reference that cannot load here (e.g. a Revit API facade) is simply not the one
                    }
                    if (type != null)
                        return type;
                }

                foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        type = loaded.GetType(fullName, false);
                    }
                    catch (Exception)
                    {
                        type = null;
                    }
                    if (type != null)
                        return type;
                }
                return null;
            }

            private void SubscribeStartupHide()
            {
                if (_panesToHide.Count == 0 || _hideSubscribed)
                    return;
                try
                {
                    // ApplicationInitialized is too early: the layout applied when the first document view opens
                    // would show the pane again. The first ViewActivated of the session is the earliest reliable point.
                    _app.ViewActivated += OnFirstViewActivated;
                    _hideSubscribed = true;
                }
                catch (Exception ex)
                {
                    Report("Could not subscribe to ViewActivated to hide " + _panesToHide.Count + " dockable pane(s) at startup: " + ex.Message);
                }
            }

            private void OnFirstViewActivated(object? sender, ViewActivatedEventArgs e)
            {
                if (!_hideSubscribed)
                    return;
                _hideSubscribed = false;

                UIApplication? uiapp = sender as UIApplication;
                foreach (DockablePaneId paneId in _panesToHide)
                {
                    try
                    {
                        if (uiapp != null)
                            uiapp.GetDockablePane(paneId).Hide();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("[RibbonBuilder] startup hide of pane " + paneId.Guid + " failed: " + ex.Message);
                    }
                }
                _panesToHide.Clear();

                // Event callbacks run inside an API context, which both subscribing and unsubscribing require.
                try
                {
                    _app.ViewActivated -= OnFirstViewActivated;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[RibbonBuilder] could not unsubscribe ViewActivated: " + ex.Message);
                }
            }

            private void BuildPanel(string tabName, XmlElement panelElement)
            {
                string panelName = Attr(panelElement, XmlNames.Name);
                if (panelName.Trim().Length == 0)
                {
                    Report("<panel> has no name and was skipped.");
                    return;
                }

                RibbonPanel panel;
                try
                {
                    panel = _app.CreateRibbonPanel(tabName, panelName);
                }
                catch (Exception ex)
                {
                    Report("Could not create panel \"" + panelName + "\": " + ex.Message);
                    return;
                }

                int pulldownIndex = 0;
                int splitIndex = 0;
                foreach (XmlElement child in Elements(panelElement))
                {
                    switch (child.Name)
                    {
                        case XmlNames.Separator:
                            panel.AddSeparator();
                            break;
                        case XmlNames.StackedItems:
                            BuildStackedItems(panel, child, ref pulldownIndex);
                            break;
                        case XmlNames.SplitButtons:
                            BuildSplitButtons(panel, child, ref splitIndex);
                            break;
                        case XmlNames.SlideoutPanel:
                            BuildSlideOut(panel, child);
                            break;
                        case XmlNames.RadioButtons:
                            BuildRadioGroup(panel, child);
                            break;
                        default:
                            Report(Describe(child) + " is not allowed inside <panel> and was ignored.");
                            break;
                    }
                }
            }

            // ---- panel structures -----------------------------------------------------------

            private void BuildStackedItems(RibbonPanel panel, XmlElement stack, ref int pulldownIndex)
            {
                List<XmlElement> items = Elements(stack).ToList();
                if (items.Count < 1 || items.Count > 3)
                {
                    Report("<stackeditems> in panel \"" + panel.Name + "\" has " + items.Count + " item(s); it must have 1 to 3 and was skipped.");
                    return;
                }

                List<XmlElement> elements = new List<XmlElement>();
                List<RibbonItemData> data = new List<RibbonItemData>();
                foreach (XmlElement item in items)
                {
                    RibbonItemData? itemData = CreateItemData(item, ref pulldownIndex);
                    if (itemData == null)
                        continue;
                    elements.Add(item);
                    data.Add(itemData);
                }
                if (data.Count == 0)
                    return;

                IList<RibbonItem> created;
                try
                {
                    switch (data.Count)
                    {
                        case 1:
                            created = new List<RibbonItem> { panel.AddItem(data[0]) };
                            break;
                        case 2:
                            created = panel.AddStackedItems(data[0], data[1]);
                            break;
                        default:
                            created = panel.AddStackedItems(data[0], data[1], data[2]);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Report("Could not add stacked items in panel \"" + panel.Name + "\": " + ex.Message);
                    return;
                }

                for (int i = 0; i < elements.Count && i < created.Count; i++)
                    ConfigureItem(elements[i], created[i]);
            }

            private void BuildSplitButtons(RibbonPanel panel, XmlElement split, ref int splitIndex)
            {
                splitIndex++;
                string internalName = splitIndex == 1 ? SplitInternalName : SplitInternalName + splitIndex;

                SplitButton? splitButton;
                try
                {
                    splitButton = panel.AddItem(new SplitButtonData(internalName, "Split")) as SplitButton;
                }
                catch (Exception ex)
                {
                    Report("Could not add the split button in panel \"" + panel.Name + "\": " + ex.Message);
                    return;
                }
                if (splitButton == null)
                {
                    Report("Revit did not return a split button for panel \"" + panel.Name + "\".");
                    return;
                }

                AddPushButtons(splitButton, split);
            }

            private void BuildSlideOut(RibbonPanel panel, XmlElement slideout)
            {
                try
                {
                    panel.AddSlideOut();
                }
                catch (Exception ex)
                {
                    // "already has a slide-out": later items still land in it, so keep going.
                    Report("Slide-out in panel \"" + panel.Name + "\": " + ex.Message);
                }

                foreach (XmlElement child in Elements(slideout))
                {
                    if (child.Name != XmlNames.Button)
                    {
                        Report(Describe(child) + " inside <slideoutpanel> was ignored (only <button> is allowed).");
                        continue;
                    }
                    PushButtonData? buttonData = CreatePushButtonData(child);
                    if (buttonData == null)
                        continue;
                    try
                    {
                        panel.AddItem(buttonData);
                    }
                    catch (Exception ex)
                    {
                        Report("Could not add " + Describe(child) + ": " + ex.Message);
                    }
                }
            }

            private void BuildRadioGroup(RibbonPanel panel, XmlElement radio)
            {
                if (!Require(radio, XmlNames.Name))
                    return;

                RadioButtonGroup? group;
                try
                {
                    group = panel.AddItem(new RadioButtonGroupData(Attr(radio, XmlNames.Name))) as RadioButtonGroup;
                }
                catch (Exception ex)
                {
                    Report("Could not add " + Describe(radio) + ": " + ex.Message);
                    return;
                }
                if (group == null)
                    return;

                foreach (XmlElement child in Elements(radio))
                {
                    if (child.Name != XmlNames.ToggleButton)
                    {
                        Report(Describe(child) + " inside " + Describe(radio) + " was ignored (only <togglebutton> is allowed).");
                        continue;
                    }
                    if (!Require(child, XmlNames.Name, XmlNames.Text))
                        continue;

                    ToggleButtonData toggle = new ToggleButtonData(Attr(child, XmlNames.Name), Attr(child, XmlNames.Text));
                    string tooltip = Attr(child, XmlNames.Tooltip);
                    if (tooltip.Length > 0)
                        toggle.ToolTip = tooltip;
                    BitmapImage? large = LoadImage(Attr(child, XmlNames.LargeImage), child, XmlNames.LargeImage);
                    if (large != null)
                        toggle.LargeImage = large;

                    try
                    {
                        group.AddItem(toggle);
                    }
                    catch (Exception ex)
                    {
                        Report("Could not add " + Describe(child) + ": " + ex.Message);
                    }
                }
            }

            /// <summary>Adds the &lt;button&gt; children of a pulldown or split button.</summary>
            private void AddPushButtons(PulldownButton parent, XmlElement container)
            {
                foreach (XmlElement child in Elements(container))
                {
                    if (child.Name != XmlNames.Button)
                    {
                        Report(Describe(child) + " inside " + Describe(container) + " was ignored (only <button> is allowed).");
                        continue;
                    }
                    PushButtonData? buttonData = CreatePushButtonData(child);
                    if (buttonData == null)
                        continue;
                    try
                    {
                        parent.AddPushButton(buttonData);
                    }
                    catch (Exception ex)
                    {
                        Report("Could not add " + Describe(child) + ": " + ex.Message);
                    }
                }
            }

            // ---- item data -----------------------------------------------------------------

            /// <summary>Creates the data for one stackable item, or null (already reported) when it is invalid.</summary>
            private RibbonItemData? CreateItemData(XmlElement item, ref int pulldownIndex)
            {
                switch (item.Name)
                {
                    case XmlNames.Button:
                        return CreatePushButtonData(item);
                    case XmlNames.PulldownButtons:
                        pulldownIndex++;
                        return CreatePulldownData(item, pulldownIndex);
                    case XmlNames.ComboBox:
                        return Require(item, XmlNames.Name) ? new ComboBoxData(Attr(item, XmlNames.Name)) : null;
                    case XmlNames.TextBox:
                        return CreateTextBoxData(item);
                    default:
                        Report(Describe(item) + " is not allowed inside <stackeditems> and was ignored.");
                        return null;
                }
            }

            private PushButtonData? CreatePushButtonData(XmlElement button)
            {
                if (!Require(button, XmlNames.Name, XmlNames.Classname, XmlNames.Text))
                    return null;

                PushButtonData data;
                try
                {
                    data = new PushButtonData(Attr(button, XmlNames.Name), Attr(button, XmlNames.Text), _assemblyPath, Attr(button, XmlNames.Classname));
                }
                catch (Exception ex)
                {
                    Report("Could not create " + Describe(button) + ": " + ex.Message);
                    return null;
                }

                string tooltip = Attr(button, XmlNames.Tooltip);
                if (tooltip.Length > 0)
                    data.ToolTip = tooltip;

                BitmapImage? image = LoadImage(Attr(button, XmlNames.Image), button, XmlNames.Image);
                if (image != null)
                    data.Image = image;

                BitmapImage? largeImage = LoadImage(Attr(button, XmlNames.LargeImage), button, XmlNames.LargeImage);
                if (largeImage != null)
                    data.LargeImage = largeImage;

                BitmapImage? tooltipImage = LoadImage(Attr(button, XmlNames.TooltipImage), button, XmlNames.TooltipImage);
                if (tooltipImage != null)
                    data.ToolTipImage = tooltipImage;

                string help = Attr(button, XmlNames.ContextHelp);
                if (help.Length > 0)
                {
                    try
                    {
                        data.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, help));
                    }
                    catch (Exception ex)
                    {
                        Report(Describe(button) + ": contexthelp \"" + help + "\" was rejected (" + ex.Message + ").");
                    }
                }

                return data;
            }

            private PulldownButtonData? CreatePulldownData(XmlElement pulldown, int index)
            {
                if (!Require(pulldown, XmlNames.Name, XmlNames.Image))
                    return null;

                string internalName = index == 1 ? PulldownInternalName : PulldownInternalName + index;
                PulldownButtonData data = new PulldownButtonData(internalName, Attr(pulldown, XmlNames.Name));

                BitmapImage? largeImage = LoadImage(Attr(pulldown, XmlNames.Image), pulldown, XmlNames.Image);
                if (largeImage != null)
                    data.LargeImage = largeImage;

                return data;
            }

            private TextBoxData? CreateTextBoxData(XmlElement textBox)
            {
                if (!Require(textBox, XmlNames.Name))
                    return null;

                TextBoxData data = new TextBoxData(Attr(textBox, XmlNames.Name));

                string longDescription = Attr(textBox, XmlNames.LongDescription);
                if (longDescription.Length > 0)
                    data.LongDescription = longDescription;

                BitmapImage? image = LoadImage(Attr(textBox, XmlNames.Image), textBox, XmlNames.Image);
                if (image != null)
                    data.Image = image;

                string tooltip = Attr(textBox, XmlNames.Tooltip);
                if (tooltip.Length > 0)
                    data.ToolTip = tooltip;

                BitmapImage? tooltipImage = LoadImage(Attr(textBox, XmlNames.TooltipImage), textBox, XmlNames.TooltipImage);
                if (tooltipImage != null)
                    data.ToolTipImage = tooltipImage;

                return data;
            }

            // ---- configuration of created items (properties that only exist after creation) ----

            private void ConfigureItem(XmlElement element, RibbonItem? created)
            {
                if (created == null)
                    return;

                switch (element.Name)
                {
                    case XmlNames.PulldownButtons:
                        if (created is PulldownButton pulldown)
                            AddPushButtons(pulldown, element);
                        break;
                    case XmlNames.ComboBox:
                        if (created is ComboBox comboBox)
                            ConfigureComboBox(element, comboBox);
                        break;
                    case XmlNames.TextBox:
                        if (created is TextBox textBox)
                            ConfigureTextBox(element, textBox);
                        break;
                }
            }

            private void ConfigureComboBox(XmlElement element, ComboBox comboBox)
            {
                string itemText = Attr(element, XmlNames.ItemText);
                if (itemText.Length > 0)
                    comboBox.ItemText = itemText;

                string longDescription = Attr(element, XmlNames.LongDescription);
                if (longDescription.Length > 0)
                    comboBox.LongDescription = longDescription;

                string tooltip = Attr(element, XmlNames.Tooltip);
                if (tooltip.Length > 0)
                    comboBox.ToolTip = tooltip;

                BitmapImage? tooltipImage = LoadImage(Attr(element, XmlNames.TooltipImage), element, XmlNames.TooltipImage);
                if (tooltipImage != null)
                    comboBox.ToolTipImage = tooltipImage;

                foreach (XmlElement member in Elements(element))
                {
                    if (member.Name != XmlNames.ComboBoxMember)
                    {
                        Report(Describe(member) + " inside " + Describe(element) + " was ignored (only <comboboxmember> is allowed).");
                        continue;
                    }
                    if (!Require(member, XmlNames.Name, XmlNames.Text))
                        continue;

                    ComboBoxMemberData memberData = new ComboBoxMemberData(Attr(member, XmlNames.Name), Attr(member, XmlNames.Text));

                    BitmapImage? image = LoadImage(Attr(member, XmlNames.Image), member, XmlNames.Image);
                    if (image != null)
                        memberData.Image = image;

                    string groupName = Attr(member, XmlNames.GroupName);
                    if (groupName.Length > 0)
                        memberData.GroupName = groupName;

                    try
                    {
                        comboBox.AddItem(memberData);
                    }
                    catch (Exception ex)
                    {
                        Report("Could not add " + Describe(member) + ": " + ex.Message);
                    }
                }

                if (_options.ComboBoxCurrentChanged != null)
                    comboBox.CurrentChanged += _options.ComboBoxCurrentChanged;
                if (_options.ComboBoxDropDownOpened != null)
                    comboBox.DropDownOpened += _options.ComboBoxDropDownOpened;
                if (_options.ComboBoxDropDownClosed != null)
                    comboBox.DropDownClosed += _options.ComboBoxDropDownClosed;
            }

            private void ConfigureTextBox(XmlElement element, TextBox textBox)
            {
                string promptText = Attr(element, XmlNames.PromptText);
                if (promptText.Length > 0)
                    textBox.PromptText = promptText;

                if (string.Equals(Attr(element, XmlNames.ShowImage), "true", StringComparison.OrdinalIgnoreCase))
                    textBox.ShowImageAsButton = true;

                if (_options.TextBoxEnterPressed != null)
                    textBox.EnterPressed += _options.TextBoxEnterPressed;
            }

            // ---- helpers ------------------------------------------------------------------

            /// <summary>Loads a PNG for a ribbon item, or returns null (reported) when the path is empty, missing or unreadable.</summary>
            private BitmapImage? LoadImage(string path, XmlElement owner, string attribute)
            {
                if (path.Length == 0)
                    return null;

                string fullPath = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(_ribbonFolder, path);
                if (!File.Exists(fullPath))
                {
                    Report(Describe(owner) + ": " + attribute + " file not found: " + fullPath);
                    return null;
                }

                try
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.UriSource = new Uri(fullPath, UriKind.Absolute);
                    image.CacheOption = BitmapCacheOption.OnLoad; // read now and release the file (Revit used to keep PNGs locked)
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
                catch (Exception ex)
                {
                    Report(Describe(owner) + ": could not load " + attribute + " " + fullPath + " (" + ex.Message + ").");
                    return null;
                }
            }

            /// <summary>Child elements only: comments (including items disabled by the editor) and whitespace never count.</summary>
            private static IEnumerable<XmlElement> Elements(XmlElement parent) => parent.ChildNodes.OfType<XmlElement>();

            /// <summary>Attribute value, or an empty string when the attribute is absent.</summary>
            private static string Attr(XmlElement element, string name) => element.GetAttribute(name);

            /// <summary>Reports and returns false when any of the named attributes is empty or missing.</summary>
            private bool Require(XmlElement element, params string[] names)
            {
                List<string>? missing = null;
                foreach (string name in names)
                {
                    if (Attr(element, name).Trim().Length == 0)
                        (missing ??= new List<string>()).Add(name);
                }
                if (missing == null)
                    return true;

                Report(Describe(element) + " is missing required attribute(s) " + string.Join(", ", missing) + " and was skipped.");
                return false;
            }

            private static string Describe(XmlElement element)
            {
                string name = element.GetAttribute(XmlNames.Name);
                return name.Length > 0 ? "<" + element.Name + " name=\"" + name + "\">" : "<" + element.Name + ">";
            }

            private void Report(string message)
            {
                _problems.Add(message);
                Debug.WriteLine("[RibbonBuilder] " + message);
                _options.Log?.Invoke(message);
            }

            private void Finish(string ribbonFile)
            {
                string summary = _problems.Count == 0
                    ? "[RibbonBuilder] " + ribbonFile + ": built with no problems."
                    : "[RibbonBuilder] " + ribbonFile + ": " + _problems.Count + " problem(s).";
                Debug.WriteLine(summary);
                _options.Log?.Invoke(summary);

                if (_problems.Count == 0 || !_options.ShowProblemsDialog)
                    return;

                string content = string.Join("\n", _problems.Take(MaxDialogLines));
                if (_problems.Count > MaxDialogLines)
                    content += "\n... and " + (_problems.Count - MaxDialogLines) + " more (see the debug output).";

                try
                {
                    Autodesk.Revit.UI.TaskDialog dialog = new Autodesk.Revit.UI.TaskDialog("Ribbon XML problems")
                    {
                        TitleAutoPrefix = false,
                        MainInstruction = System.IO.Path.GetFileName(ribbonFile) + ": " + _problems.Count + " problem(s). The affected items were skipped.",
                        MainContent = content,
                    };
                    dialog.Show();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[RibbonBuilder] could not show the problems dialog: " + ex.Message);
                }
            }
        }
    }
}


////////////////////////////////////////////
// 3.1.0 2026-10-08 — <dockablepane> element: registers IDockablePaneProvider panes from the .ribbon file (startshidden, TryGetDockablePane registry) by Orlando Hernandez
// 3.0.0 2026-10-01 — hardened version Updated by Orlando Hernandez, options, single-file drop-in
// 2.0.0 2024-08-22 - Original Version Created 06/01/21 by Alana Bianes
////////////////////////////////////////////
