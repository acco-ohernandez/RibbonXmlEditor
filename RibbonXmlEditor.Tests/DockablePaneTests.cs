using System.ComponentModel;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.Services;
using RibbonXmlEditor.ViewModels.Fields;
using Xunit;
using static RibbonXmlEditor.Tests.ViewModelTests;

namespace RibbonXmlEditor.Tests;

/// <summary>
/// The &lt;dockablepane&gt; element (RibbonBuilder 3.1): schema, round-trip, validator rules, scanner and view-model behaviour.
/// </summary>
public class DockablePaneTests
{
    private const string GoodGuid = "ACC0D0C5-11B2-4A2B-9E77-3F1A6C5B2D41";
    private const string PaneClass = "RevitRibbon_MainSourceCode_Resources.Forms.LinkLibrary_Pane";

    // An Images folder that certainly exists, so missing-image checks are not downgraded.
    private static readonly ValidationContext Ctx = new(null, null, Path.GetTempPath());

    private static string Pane(string name = "ACCODocsLibrary", string guid = GoodGuid, string title = "ACCO Link Library",
        string classname = PaneClass, string startshidden = "true")
        => $"<dockablepane name=\"{name}\" guid=\"{guid}\" title=\"{title}\" classname=\"{classname}\" startshidden=\"{startshidden}\"/>";

    private static string Btn(string name)
        => $"<button name=\"{name}\" classname=\"Ns.Cmd_X\" text=\"{name}\" tooltip=\"\" image=\"\" largeimage=\"\" tooltipimage=\"\" contexthelp=\"\"/>";

    private static RibbonDocument TabWith(string tabInner)
        => RibbonXmlReader.Parse($"<tab name=\"T\">{tabInner}</tab>").Document;

    private static List<Issue> Validate(RibbonDocument doc, ValidationContext? ctx = null) => RibbonValidator.Validate(doc, ctx ?? Ctx);

    private static IEnumerable<string> Rules(IEnumerable<Issue> issues) => issues.Select(i => i.RuleId);

    // ---- schema ------------------------------------------------------------------------

    [Fact]
    public void Schema_DescribesTheElementAsTheParserReadsIt()
    {
        var def = RibbonSchema.ByKind(ElementKind.DockablePane);
        Assert.Equal("dockablepane", def.XmlName);
        Assert.Equal(new[] { "name", "guid", "title", "classname", "startshidden" }, def.Attributes.Select(a => a.XmlName));
        Assert.Equal(new[] { true, true, false, true, false }, def.Attributes.Select(a => a.IsRequired));
        Assert.Equal(FieldKind.Guid, def.GetAttribute("guid")!.Kind);
        Assert.Equal(FieldKind.PaneClassName, def.GetAttribute("classname")!.Kind);
        Assert.Equal(FieldKind.TrueOrEmpty, def.GetAttribute("startshidden")!.Kind);
        Assert.Empty(def.AllowedChildren);
        Assert.Equal(0, def.MinChildren);

        Assert.True(RibbonSchema.ByKind(ElementKind.Tab).Allows(ElementKind.DockablePane));
        Assert.False(RibbonSchema.ByKind(ElementKind.Panel).Allows(ElementKind.DockablePane));
        Assert.False(RibbonSchema.IsPanelItem(ElementKind.DockablePane));
    }

    // ---- round trip --------------------------------------------------------------------

    [Fact]
    public void Pane_RoundTrips_WithAttributeOrder_RawNewlineInTitle_AndDisabledAsComment()
    {
        var first = RibbonXmlReader.Parse(
            "<tab name=\"T\">" +
            Pane(title: "ACCO&#10;Link Library") +
            $"<panel name=\"P\"><stackeditems>{Btn("a")}</stackeditems></panel>" +
            "</tab>");
        Assert.Empty(first.Issues);
        Assert.False(first.NeedsRepairSave);

        var pane = first.Document.Tab.Children[0];
        Assert.Equal(ElementKind.DockablePane, pane.Kind);
        Assert.Equal("ACCO\nLink Library", pane["title"]);

        var xml = RibbonXmlWriter.ToXmlString(first.Document);
        int at(string s) => xml.IndexOf(s, StringComparison.Ordinal);
        Assert.True(at("<dockablepane") > 0 && at("<dockablepane") < at("<panel"), "pane keeps its position before the panel");
        Assert.True(at("name=\"ACCODocsLibrary\"") < at("guid=\"") && at("guid=\"") < at("title=\"")
                    && at("title=\"") < at("classname=\"" + PaneClass) && at("classname=\"" + PaneClass) < at("startshidden=\""),
            "attributes are written in schema order:\n" + xml);
        Assert.Contains("title=\"ACCO\r\nLink Library\"", xml); // raw line break inside the attribute, as the parser keeps it

        var second = RibbonXmlReader.Parse(xml);
        Assert.Empty(second.Issues);
        RoundTripTests.AssertDocumentsEqual(first.Document, second.Document);
        Assert.Equal(xml, RibbonXmlWriter.ToXmlString(second.Document));

        // Disabled: a childless element directly under <tab> becomes a comment and comes back disabled.
        pane.IsDisabled = true;
        var disabledXml = RibbonXmlWriter.ToXmlString(first.Document);
        Assert.Contains("<dockablepane", disabledXml.Split("<!--")[1].Split("-->")[0]);
        var third = RibbonXmlReader.Parse(disabledXml);
        Assert.Empty(third.Issues);
        Assert.True(third.Document.Tab.Children[0].IsDisabled);
        Assert.Equal(ElementKind.DockablePane, third.Document.Tab.Children[0].Kind);
        Assert.Equal("ACCODocsLibrary", third.Document.Tab.Children[0].Name);
        Assert.Equal(disabledXml, RibbonXmlWriter.ToXmlString(third.Document));
    }

    [Fact]
    public void Writer_EmitsEveryPaneAttribute_EvenWhenEmpty()
    {
        var doc = RibbonDocument.CreateBlank("T");
        doc.Tab.AddChild(new RibbonNode(ElementKind.DockablePane));
        var xml = RibbonXmlWriter.ToXmlString(doc);
        foreach (var a in RibbonSchema.ByKind(ElementKind.DockablePane).Attributes)
            Assert.Contains($"{a.XmlName}=\"\"", xml);
    }

    [Fact]
    public void MissingPaneAttribute_IsRepairedOnSave()
    {
        var result = RibbonXmlReader.Parse($"<tab name=\"T\"><dockablepane name=\"p\" guid=\"{GoodGuid}\" classname=\"{PaneClass}\"/></tab>");
        Assert.True(result.NeedsRepairSave);
        Assert.Equal(new[] { "title", "startshidden" },
            result.Issues.Where(i => i.RuleId == "load.missing-attribute").Select(i => i.AttributeName));
        Assert.Contains("startshidden=\"\"", RibbonXmlWriter.ToXmlString(result.Document));
    }

    // ---- validator ---------------------------------------------------------------------

    [Fact]
    public void CleanPane_HasNoIssues_AndIsNotAnEmptyContainer()
    {
        var doc = TabWith(Pane() + $"<panel name=\"P\"><stackeditems>{Btn("a")}</stackeditems></panel>");
        Assert.Empty(Validate(doc));

        // A tab holding only a pane is fine too.
        Assert.Empty(Validate(TabWith(Pane())));
    }

    [Fact]
    public void RequiredPaneAttributes_AreErrors_TitleAndStartsHiddenAreOptional()
    {
        var doc = TabWith(Pane(name: "", guid: "", title: "", classname: "", startshidden: ""));
        var required = Validate(doc).Where(i => i.RuleId == "required").ToList();
        Assert.Equal(new[] { "name", "guid", "classname" }, required.Select(i => i.AttributeName));
        Assert.All(required, i => Assert.True(i.IsError));
        Assert.Equal(3, Validate(doc).Count); // nothing else fires on an otherwise empty pane
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("ACC0D0C5-11B2-4A2B-9E77-3F1A6C5B2D4")] // one digit short
    [InlineData("ACC0D0C5-11B2-4A2B-9E77-3F1A6C5B2D4G")] // G is not hex
    public void InvalidGuid_IsError(string guid)
    {
        var issue = Assert.Single(Validate(TabWith(Pane(guid: guid))));
        Assert.Equal("guid.invalid", issue.RuleId);
        Assert.True(issue.IsError);
        Assert.Equal("guid", issue.AttributeName);
    }

    [Theory]
    [InlineData("{ACC0D0C5-11B2-4A2B-9E77-3F1A6C5B2D41}")]
    [InlineData("(ACC0D0C5-11B2-4A2B-9E77-3F1A6C5B2D41)")]
    [InlineData("ACC0D0C511B24A2B9E773F1A6C5B2D41")]
    public void GuidInAnotherStandardFormat_IsOnlyAFormatWarning(string guid)
    {
        var issue = Assert.Single(Validate(TabWith(Pane(guid: guid))));
        Assert.Equal("guid.format", issue.RuleId);
        Assert.False(issue.IsError);
        Assert.Contains(GoodGuid, issue.Message);
    }

    [Theory]
    [InlineData(GoodGuid)]
    [InlineData("acc0d0c5-11b2-4a2b-9e77-3f1a6c5b2d41")]
    [InlineData(" ACC0D0C5-11B2-4A2B-9E77-3F1A6C5B2D41 ")] // the parser trims
    public void CanonicalGuid_AnyCase_IsClean(string guid)
    {
        Assert.Empty(Validate(TabWith(Pane(guid: guid))));
    }

    [Fact]
    public void AllZeroGuid_IsAPlaceholderWarning()
    {
        var issue = Assert.Single(Validate(TabWith(Pane(guid: "00000000-0000-0000-0000-000000000000"))));
        Assert.Equal("guid.empty", issue.RuleId);
        Assert.False(issue.IsError);
    }

    [Fact]
    public void DuplicatePaneNames_AreCaseInsensitiveErrors_DistinctNamesAreFine()
    {
        var doc = TabWith(Pane(name: "Docs", guid: GoodGuid) + Pane(name: "docs", guid: "11111111-2222-3333-4444-555555555555"));
        var dups = Validate(doc).Where(i => i.RuleId == "name.duplicate").ToList();
        Assert.Equal(2, dups.Count);
        Assert.All(dups, i => { Assert.True(i.IsError); Assert.Contains("Dockable pane name", i.Message); });

        var fine = TabWith(Pane(name: "Docs", guid: GoodGuid) + Pane(name: "Other", guid: "11111111-2222-3333-4444-555555555555"));
        Assert.Empty(Validate(fine));

        // A pane and a panel may share a name: they live in different registries.
        var mixed = TabWith(Pane(name: "Same") + $"<panel name=\"Same\"><stackeditems>{Btn("a")}</stackeditems></panel>");
        Assert.Empty(Validate(mixed));

        // Disabled duplicates do not count.
        doc.Tab.Children[1].IsDisabled = true;
        Assert.Empty(Validate(doc));
    }

    [Fact]
    public void DuplicateGuids_AreWarnings()
    {
        var doc = TabWith(Pane(name: "A") + Pane(name: "B"));
        var dups = Validate(doc).Where(i => i.RuleId == "guid.duplicate").ToList();
        Assert.Equal(2, dups.Count);
        Assert.All(dups, i => { Assert.False(i.IsError); Assert.Equal("guid", i.AttributeName); });
    }

    [Fact]
    public void PaneInsidePanel_IsNotAllowed_PaneUnderTabIs()
    {
        var wrong = TabWith($"<panel name=\"P\">{Pane()}</panel>");
        var issue = Assert.Single(Validate(wrong));
        Assert.Equal("child.notallowed", issue.RuleId);
        Assert.True(issue.IsError);
        Assert.Equal(ElementKind.DockablePane, issue.Node!.Kind);
        Assert.Contains("<dockablepane> is not allowed inside <panel>", issue.Message);

        var right = TabWith($"<panel name=\"P\"><stackeditems>{Btn("a")}</stackeditems></panel>" + Pane());
        Assert.Empty(Validate(right));
    }

    [Fact]
    public void PaneClass_IsNotCheckedAgainstTheCommandList_ButAgainstThePaneScan()
    {
        // Command classes known, no pane scan: the pane class is free text.
        var commandsOnly = new ValidationContext(new HashSet<string> { "Ns.Cmd_Known" }, "Tab.dll", Path.GetTempPath());
        Assert.Empty(Validate(TabWith(Pane()), commandsOnly));

        // Pane scan ran and the class is missing: a warning, not an error (Revit reports and skips the pane).
        var paneScan = new ValidationContext(new HashSet<string> { "Ns.Cmd_Known" }, "Tab.dll", Path.GetTempPath(),
            new HashSet<string> { "Other.Pane" });
        var issue = Assert.Single(Validate(TabWith(Pane()), paneScan));
        Assert.Equal("paneclass.unknown", issue.RuleId);
        Assert.False(issue.IsError);
        Assert.Contains("Tab.dll", issue.Message);
        Assert.DoesNotContain("classname.unknown", Rules(Validate(TabWith(Pane()), paneScan)));

        // Found: clean. The command list never sees the pane class.
        var found = paneScan with { KnownPaneClasses = new HashSet<string> { PaneClass } };
        Assert.Empty(Validate(TabWith(Pane()), found));
    }

    [Fact]
    public void MalformedPaneClass_IsAFormatWarning()
    {
        var issue = Assert.Single(Validate(TabWith(Pane(classname: "no namespace here"))));
        Assert.Equal("classname.format", issue.RuleId);
        Assert.False(issue.IsError);
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("yes", false)]
    public void StartsHidden_OddValue_IsWarning(string value, bool stillTrueForBuilder3)
    {
        var issue = Assert.Single(Validate(TabWith(Pane(startshidden: value))));
        Assert.Equal("flag.value", issue.RuleId);
        Assert.False(issue.IsError);
        Assert.Equal(stillTrueForBuilder3, issue.Message.Contains("builder 3.0+"));
        Assert.Equal(!stillTrueForBuilder3, issue.Message.Contains("as false"));
    }

    // ---- scanner -----------------------------------------------------------------------

    public static IEnumerable<object[]> ResourcesDlls() => new[]
    {
        new object[] { Path.Combine(TestPaths.RepoRoot, @"03.Engineering_Mechanical_Tab\bin\Debug\2025\RevitRibbon_MainSourceCode_Resources.dll") },
        new object[] { Path.Combine(TestPaths.DeployedRoot, @"Engineering\Mechanical\2025\RevitRibbon_MainSourceCode_Resources.dll") },
    };

    [Theory]
    [MemberData(nameof(ResourcesDlls))]
    public void ScanPaneProviders_FindsThePaneClass_InTheResourcesDll(string dll)
    {
        if (!File.Exists(dll))
            return;

        var result = CommandClassScanner.ScanPaneProviders(dll);
        Assert.Null(result.Error);
        Assert.Contains(PaneClass, result.ClassNames);
        Assert.Equal(result.ClassNames.OrderBy(n => n, StringComparer.Ordinal), result.ClassNames);

        // The same DLL holds no ribbon commands, and the pane scan lists no commands: the two scans are independent.
        Assert.DoesNotContain("RevitRibbon_MainSourceCode.Cmd_About", result.ClassNames);
    }

    [Fact]
    public void ScanPaneProviders_OnTheTabDll_FindsNothingButDoesNotFail()
    {
        var tabDll = Path.Combine(TestPaths.RepoRoot, @"03.Engineering_Mechanical_Tab\bin\Debug\2025\03.Engineering_Mechanical_Tab.dll");
        if (!File.Exists(tabDll))
            return;

        var panes = CommandClassScanner.ScanPaneProviders(tabDll);
        Assert.Null(panes.Error);
        Assert.DoesNotContain(PaneClass, panes.ClassNames);

        // And the pane DLL list pairs the tab DLL with the Resources DLL next to it.
        var dlls = CommandClassScanner.PaneDllsFor(tabDll);
        Assert.Equal(2, dlls.Count);
        Assert.Equal(tabDll, dlls[0]);
        Assert.Equal(CommandClassScanner.ResourcesDllName, Path.GetFileName(dlls[1]));
    }

    [Fact]
    public void PaneDllsFor_ListsOnlyTheTabDll_WhenNoResourcesDllIsPresent()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RibbonXmlEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tab = Path.Combine(dir, "X_Tab.dll");
            File.WriteAllText(tab, "not really");
            Assert.Equal(new[] { tab }, CommandClassScanner.PaneDllsFor(tab));

            File.WriteAllText(Path.Combine(dir, CommandClassScanner.ResourcesDllName), "not really");
            Assert.Equal(2, CommandClassScanner.PaneDllsFor(tab).Count);

            // Scanning a non-assembly reports instead of throwing.
            Assert.NotNull(CommandClassScanner.ScanPaneProviders(tab).Error);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ---- view models -------------------------------------------------------------------

    private static List<string> Record(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName ?? string.Empty);
        return names;
    }

    [Fact]
    public void Tree_AddsAPaneToTheTab_WithGlyphCaptionAndStrip()
    {
        Sta(() =>
        {
            var (_, editor, doc) = Blank();
            var root = doc.Root;
            var option = root.AddChildOptions.Single(o => o.Kind == ElementKind.DockablePane);
            Assert.Equal("Add dockable pane", option.ButtonText);
            Assert.True(option.IsEnabled);
            Assert.False(root.HasDockablePanes);

            var rootNames = Record(root);
            var pane = root.AddChild(ElementKind.DockablePane);

            Assert.Same(pane, editor.SelectedNode);
            Assert.True(root.HasDockablePanes);
            Assert.Contains("HasDockablePanes", rootNames);
            Assert.Equal("DP", pane.Glyph);
            Assert.Equal("Dockable pane", pane.Header);
            Assert.Equal("(dockable pane)", pane.Caption);
            Assert.False(pane.IsLargeInPreview);
            Assert.False(pane.CanAddChildren);
            Assert.Empty(pane.AddChildOptions);
            Assert.True(pane.CanToggleEnabled);

            // Caption: title, then name, like the parser; the tree header always uses the name.
            var paneNames = Record(pane);
            pane.GetField("name")!.Value = "ACCODocsLibrary";
            Assert.Equal("Dockable pane: ACCODocsLibrary", pane.Header);
            Assert.Equal("ACCODocsLibrary", pane.Caption);
            pane.GetField("title")!.Value = "ACCO Link Library";
            Assert.Equal("ACCO Link Library", pane.Caption);
            Assert.Equal("Dockable pane: ACCODocsLibrary", pane.Header);
            Assert.Contains("Caption", paneNames);

            Assert.False(pane.IsStartsHidden);
            paneNames.Clear();
            ((TrueOrEmptyField)pane.GetField("startshidden")!).IsChecked = true;
            Assert.True(pane.IsStartsHidden);
            Assert.Contains("IsStartsHidden", paneNames);
            Assert.Equal("true", pane.Node["startshidden"]);

            // The property panel got the two new field kinds.
            Assert.IsType<GuidField>(pane.GetField("guid"));
            Assert.IsType<PaneClassNameField>(pane.GetField("classname"));

            // Selecting from the preview strip works like any other item.
            doc.Root.IsSelected = true;
            pane.IsSelected = false;
            pane.SelectCommand.Execute(null);
            Assert.Same(pane, editor.SelectedNode);

            pane.DeleteCommand.Execute(null);
            Assert.False(root.HasDockablePanes);
        });
    }

    [Fact]
    public void GuidField_NewFillsACanonicalUpperCaseGuid_AndValidityFollowsTheText()
    {
        Sta(() =>
        {
            var (_, editor, doc) = Blank();
            var pane = doc.Root.AddChild(ElementKind.DockablePane);
            var guid = (GuidField)pane.GetField("guid")!;
            Assert.True(guid.IsValid); // empty counts as valid here; "required" is the validator's job

            guid.NewGuidCommand.Execute(null); // empty value: no confirmation dialog
            Assert.True(Guid.TryParse(guid.Value, out var parsed));
            Assert.Equal(parsed.ToString("D").ToUpperInvariant(), guid.Value);
            Assert.True(guid.IsValid);

            guid.Value = "nope";
            Assert.False(guid.IsValid);
            Assert.True(guid.IsInvalid);

            pane.GetField("name")!.Value = "P";
            pane.GetField("classname")!.Value = "Ns.Pane";
            editor.Revalidate();
            Assert.Contains(editor.Issues, i => i.Issue.RuleId == "guid.invalid" && i.AttributeName == "guid");
            Assert.True(guid.HasError);
        });
    }

    [Fact]
    public void DuplicatingAPane_WarnsAboutTheSharedGuid()
    {
        Sta(() =>
        {
            var (_, editor, doc) = Blank();
            var pane = doc.Root.AddChild(ElementKind.DockablePane);
            pane.GetField("name")!.Value = "Docs";
            pane.GetField("guid")!.Value = GoodGuid;
            pane.GetField("classname")!.Value = PaneClass;
            editor.Revalidate();
            Assert.DoesNotContain(editor.Issues, i => i.Issue.RuleId.StartsWith("guid."));

            pane.DuplicateCommand.Execute(null);
            Assert.Equal("Docs Copy", doc.Root.Children[pane.Node.IndexInParent + 1].Node.Name);
            editor.Revalidate();
            Assert.Equal(2, editor.Issues.Count(i => i.Issue.RuleId == "guid.duplicate"));
            Assert.DoesNotContain(editor.Issues, i => i.Issue.RuleId == "name.duplicate");
        });
    }

    // ---- "Add dockable toggle" (pane + launcher button in one step) ---------------------

    [Fact]
    public void AddDockableToggle_CreatesLinkedPaneAndButton()
    {
        Sta(() =>
        {
            var (_, editor, doc) = Blank();
            var panel = doc.Root.Children[0];
            doc.IsDirty = false;
            Assert.True(panel.AddDockableToggleCommand.CanExecute(null));

            var pane = panel.AddDockableToggle();

            // The pane: first under the tab, linked defaults, fresh canonical GUID, starts hidden.
            Assert.Same(pane, doc.Root.Children[0]);
            Assert.Equal(ElementKind.DockablePane, pane.Node.Kind);
            Assert.Equal("NewPane", pane.Node.Name);
            Assert.Equal("New Pane", pane.Node["title"]);
            Assert.Equal("true", pane.Node["startshidden"]);
            Assert.Equal("", pane.Node["classname"]);
            Assert.True(Guid.TryParse(pane.Node["guid"], out var guid));
            Assert.Equal(guid.ToString("D").ToUpperInvariant(), pane.Node["guid"]);
            Assert.NotEqual(Guid.Empty, guid);

            // The launcher: a large button in a new stack at the end of this panel.
            var stack = panel.Children[^1];
            Assert.Equal(ElementKind.StackedItems, stack.Node.Kind);
            var button = Assert.Single(stack.Children);
            Assert.Equal(ElementKind.Button, button.Node.Kind);
            Assert.Equal("btn_NewPane", button.Node.Name);
            Assert.Equal("New Pane", button.Node["text"]);
            Assert.Equal("", button.Node["classname"]);
            Assert.True(button.IsLargeInPreview);

            Assert.Same(pane, editor.SelectedNode);
            Assert.True(pane.IsSelected);
            Assert.True(doc.IsDirty);
            Assert.True(doc.Root.HasDockablePanes);

            // Only the two class names are still missing (plus the blank document's own starter button).
            editor.Revalidate();
            var required = editor.Issues.Where(i => i.Issue.RuleId == "required").ToList();
            Assert.Equal(editor.Issues.Count, required.Count);
            Assert.Contains(required, i => i.Node == pane && i.AttributeName == "classname");
            Assert.Contains(required, i => i.Node == button && i.AttributeName == "classname");
            Assert.Equal(3, required.Count); // + the starter button of Blank()

            // Round trip: both elements are written and read back cleanly.
            var again = RibbonXmlReader.Parse(RibbonXmlWriter.ToXmlString(doc.Model));
            Assert.Empty(again.Issues);
            Assert.Equal(ElementKind.DockablePane, again.Document.Tab.Children[0].Kind);
            Assert.Contains(again.Document.Tab.Descendants(), n => n.Kind == ElementKind.Button && n.Name == "btn_NewPane");
        });
    }

    [Fact]
    public void AddDockableToggle_NamesStayUnique_AndPanesStayGrouped()
    {
        Sta(() =>
        {
            var (_, _, doc) = Blank();
            var panel = doc.Root.Children[0];

            var p1 = panel.AddDockableToggle();
            var p2 = panel.AddDockableToggle();
            // Case-insensitive uniqueness: rename the first pane and add a third.
            p1.GetField("name")!.Value = "newpane3";
            var p3 = panel.AddDockableToggle();

            Assert.Equal("NewPane2", p2.Node.Name);
            Assert.Equal("New Pane 2", p2.Node["title"]);
            Assert.Equal("NewPane", p3.Node.Name); // "NewPane" became free again when p1 was renamed
            Assert.NotEqual(p1.Node["guid"], p2.Node["guid"]);

            // Panes are grouped at the top, in creation order, panels after them.
            Assert.Equal(new[] { ElementKind.DockablePane, ElementKind.DockablePane, ElementKind.DockablePane, ElementKind.Panel },
                doc.Root.Children.Select(c => c.Node.Kind));
            Assert.Same(p1, doc.Root.Children[0]);
            Assert.Same(p2, doc.Root.Children[1]);
            Assert.Same(p3, doc.Root.Children[2]);

            Assert.Equal(3, panel.Children.Count(c => c.Node.Kind == ElementKind.StackedItems
                                                       && c.Children.Any(b => b.Node.Name.StartsWith("btn_NewPane", StringComparison.Ordinal))));
            Assert.Contains(panel.Children, c => c.Children.Any(b => b.Node.Name == "btn_NewPane2"));
        });
    }

    [Fact]
    public void AddDockableToggle_IsOfferedOnPanelsOnly()
    {
        Sta(() =>
        {
            var (_, _, doc) = Blank();
            var panel = doc.Root.Children[0];
            var stack = panel.Children[0];
            var button = stack.Children[0];

            Assert.True(panel.CanAddDockableToggle);
            foreach (var other in new[] { doc.Root, stack, button })
            {
                Assert.False(other.CanAddDockableToggle);
                Assert.False(other.AddDockableToggleCommand.CanExecute(null));
                Assert.Throws<InvalidOperationException>(() => other.AddDockableToggle());
            }
        });
    }

    [Fact]
    public void Template_CarriesADisabledPaneExample()
    {
        var result = TemplateProvider.LoadButtonStructureTemplate();
        Assert.Empty(result.Issues);
        var pane = Assert.Single(result.Document.Tab.Children, n => n.Kind == ElementKind.DockablePane);
        Assert.True(pane.IsDisabled);
        Assert.Equal("TemplatePane", pane.Name);
        Assert.Equal("true", pane["startshidden"]);

        // Disabled, so the placeholder GUID raises nothing; enabling it brings the placeholder warning.
        Assert.DoesNotContain(RibbonValidator.Validate(result.Document, Ctx), i => i.RuleId.StartsWith("guid."));
        pane.IsDisabled = false;
        Assert.Contains(RibbonValidator.Validate(result.Document, Ctx), i => i.RuleId == "guid.empty");
    }
}
