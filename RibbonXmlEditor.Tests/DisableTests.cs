using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.Services;
using Xunit;
using static RibbonXmlEditor.Tests.ViewModelTests;

namespace RibbonXmlEditor.Tests;

/// <summary>Disabled items: written as XML comments, read back, validated and toggled.</summary>
public class DisableTests
{
    private static readonly ValidationContext Ctx = new(null, null, Path.GetTempPath());

    private static string Btn(string name, string classname = "Ns.Cmd_X", string tooltip = "")
        => $"<button name=\"{name}\" classname=\"{classname}\" text=\"{name}\" tooltip=\"{tooltip}\" image=\"\" largeimage=\"\" tooltipimage=\"\" contexthelp=\"\"/>";

    private static RibbonDocument Doc(string stackInner)
        => RibbonXmlReader.Parse($"<tab name=\"T\"><panel name=\"P\"><stackeditems>{stackInner}</stackeditems></panel></tab>").Document;

    [Fact]
    public void DisabledNode_IsWrittenAsComment_AndReadBack()
    {
        var doc = Doc(Btn("One") + Btn("Two"));
        var stack = doc.Tab.Children[0].Children[0];
        stack.Children[1].IsDisabled = true;
        stack.Children[1]["tooltip"] = "line one\nline two";

        var xml = RibbonXmlWriter.ToXmlString(doc);
        Assert.Contains("<!--", xml);
        Assert.Contains("name=\"Two\"", xml);
        Assert.Contains("<button", xml.Split("<!--")[1].Split("-->")[0]); // the block is inside the comment

        var again = RibbonXmlReader.Parse(xml);
        Assert.Empty(again.Issues);
        Assert.False(again.NeedsRepairSave);
        var stack2 = again.Document.Tab.Children[0].Children[0];
        Assert.Equal(2, stack2.Children.Count);
        Assert.False(stack2.Children[0].IsDisabled);
        Assert.True(stack2.Children[1].IsDisabled);
        Assert.Equal("Two", stack2.Children[1].Name);
        Assert.Equal("line one\nline two", stack2.Children[1]["tooltip"]);

        // Writing again is stable.
        Assert.Equal(xml, RibbonXmlWriter.ToXmlString(again.Document));
    }

    [Fact]
    public void DisabledContainer_KeepsItsChildrenInsideTheComment()
    {
        var doc = RibbonXmlReader.Parse(
            $"<tab name=\"T\"><panel name=\"P\"><stackeditems><pulldownbuttons name=\"pd\" image=\"C:\\x.png\">{Btn("A")}{Btn("B")}</pulldownbuttons></stackeditems></panel></tab>").Document;
        var pulldown = doc.Tab.Children[0].Children[0].Children[0];
        pulldown.IsDisabled = true;
        Assert.True(pulldown.Children[0].IsEffectivelyDisabled);

        var xml = RibbonXmlWriter.ToXmlString(doc);
        Assert.Equal(1, xml.Split("<!--").Length - 1); // exactly one comment, children are inside it

        var again = RibbonXmlReader.Parse(xml).Document;
        var pd2 = again.Tab.Children[0].Children[0].Children[0];
        Assert.True(pd2.IsDisabled);
        Assert.Equal(2, pd2.Children.Count);
        Assert.False(pd2.Children[0].IsDisabled);           // own flag false...
        Assert.True(pd2.Children[0].IsEffectivelyDisabled); // ...but disabled through the parent
    }

    [Fact]
    public void HandCommentedButton_LoadsAsDisabled_ProseCommentIsStillDropped()
    {
        var result = RibbonXmlReader.Parse(
            "<tab name=\"T\"><panel name=\"P\"><stackeditems>" +
            Btn("Live") +
            "<!-- " + Btn("Old") + " -->" +
            "<!-- just a note -->" +
            "</stackeditems></panel></tab>");

        var stack = result.Document.Tab.Children[0].Children[0];
        Assert.Equal(2, stack.Children.Count);
        Assert.True(stack.Children[1].IsDisabled);
        Assert.Equal("Old", stack.Children[1].Name);
        Assert.Single(result.Issues, i => i.RuleId == "load.comment-dropped");
    }

    [Fact]
    public void Validator_CountsOnlyEnabledItems_AndWarnsAboutLegacyBuilders()
    {
        // 3 enabled + 1 disabled: valid for 3.0, warning for older builders.
        var doc = Doc(Btn("A") + Btn("B") + Btn("C") + Btn("D"));
        var stack = doc.Tab.Children[0].Children[0];
        stack.Children[3].IsDisabled = true;
        var issues = RibbonValidator.Validate(doc, Ctx);
        Assert.DoesNotContain(issues, i => i.RuleId == "stacked.count");
        Assert.Contains(issues, i => i.RuleId == "stacked.legacycount" && !i.IsError);

        // All disabled: a warning, not an error.
        foreach (var c in stack.Children) c.IsDisabled = true;
        issues = RibbonValidator.Validate(doc, Ctx);
        Assert.DoesNotContain(issues, i => i.IsError);
        Assert.Contains(issues, i => i.RuleId == "stacked.alldisabled");

        // A disabled item is not validated (invalid classname, duplicate name are ignored)...
        var doc2 = Doc(Btn("Same") + Btn("Same", classname: "") );
        doc2.Tab.Children[0].Children[0].Children[1].IsDisabled = true;
        issues = RibbonValidator.Validate(doc2, Ctx);
        Assert.Empty(issues);

        // ...except for "--", which cannot live inside a comment.
        doc2.Tab.Children[0].Children[0].Children[1]["tooltip"] = "a -- b";
        issues = RibbonValidator.Validate(doc2, Ctx);
        var dd = Assert.Single(issues);
        Assert.Equal("disabled.doubledash", dd.RuleId);
        Assert.False(dd.IsError);
    }

    [Fact]
    public void ToggleEnabled_Rules_AndTreeState()
    {
        Sta(() =>
        {
            var (_, _, doc) = Blank();
            var panel = doc.Root.Children[0];
            var stack = panel.Children[0];
            var button = stack.Children[0];
            doc.IsDirty = false;

            Assert.False(doc.Root.ToggleEnabledCommand.CanExecute(null)); // the tab cannot be disabled
            Assert.True(button.ToggleEnabledCommand.CanExecute(null));
            Assert.Equal("_Disable", button.ToggleEnabledHeader);

            button.ToggleEnabledCommand.Execute(null);

            Assert.True(button.IsDisabled);
            Assert.True(doc.IsDirty);
            Assert.EndsWith("(disabled)", button.Header);
            Assert.Equal("_Enable", button.ToggleEnabledHeader);
            Assert.False(button.IsEnabledInRevit);

            // Disabling the stack disables the button through its parent and locks its toggle.
            stack.IsDisabled = true;
            Assert.True(button.IsEffectivelyDisabled);
            Assert.False(button.CanToggleEnabled);
            Assert.False(button.ToggleEnabledCommand.CanExecute(null));
            Assert.True(button.IsDisabled); // own flag untouched

            stack.IsEnabledInRevit = true;
            Assert.True(button.CanToggleEnabled);

            // Duplicate keeps the flag.
            button.DuplicateCommand.Execute(null);
            Assert.True(stack.Children[1].IsDisabled);
        });
    }

    [Fact]
    public void ProductionFiles_HaveNoDisabledNodes()
    {
        foreach (var rel in TestPaths.ProductionRibbons)
        {
            var path = Path.Combine(TestPaths.RepoRoot, rel);
            if (!File.Exists(path)) continue;
            var doc = RibbonXmlReader.Load(path).Document;
            Assert.DoesNotContain(doc.Tab.DescendantsAndSelf(), n => n.IsDisabled);
        }
    }
}
