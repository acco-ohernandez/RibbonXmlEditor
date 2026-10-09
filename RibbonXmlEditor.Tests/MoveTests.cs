using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.Services;
using Xunit;
using static RibbonXmlEditor.Tests.ViewModelTests;

namespace RibbonXmlEditor.Tests;

/// <summary>Drag-and-drop moves: the model re-parenting and the view-model rules the tree enforces.</summary>
public class MoveTests
{
    private static string Btn(string name)
        => $"<button name=\"{name}\" classname=\"Ns.Cmd_X\" text=\"{name}\" tooltip=\"\" image=\"\" largeimage=\"\" tooltipimage=\"\" contexthelp=\"\"/>";

    // ---- model -------------------------------------------------------------------------

    [Fact]
    public void Model_MoveTo_ReordersWithinParent_UsingPreMoveIndexes()
    {
        var doc = RibbonXmlReader.Parse($"<tab name=\"T\"><panel name=\"P\"><stackeditems>{Btn("a")}{Btn("b")}{Btn("c")}</stackeditems></panel></tab>").Document;
        var stack = doc.Tab.Children[0].Children[0];
        var (a, b, c) = (stack.Children[0], stack.Children[1], stack.Children[2]);

        a.MoveTo(stack, 3);                                   // "after c" (index past the end)
        Assert.Equal(new[] { b, c, a }, stack.Children);
        Assert.Same(stack, a.Parent);

        a.MoveTo(stack, 0);                                   // back to the front
        Assert.Equal(new[] { a, b, c }, stack.Children);

        c.MoveTo(stack, 1);                                   // "before b"
        Assert.Equal(new[] { a, c, b }, stack.Children);

        b.MoveTo(stack, 1);                                   // "before c" from behind it
        Assert.Equal(new[] { a, b, c }, stack.Children);
    }

    [Fact]
    public void Model_MoveTo_AcrossParents_AndRefusesItself()
    {
        var doc = RibbonXmlReader.Parse(
            $"<tab name=\"T\"><panel name=\"P\"><stackeditems>{Btn("a")}</stackeditems><stackeditems>{Btn("b")}</stackeditems></panel></tab>").Document;
        var panel = doc.Tab.Children[0];
        var (s1, s2) = (panel.Children[0], panel.Children[1]);
        var a = s1.Children[0];

        a.MoveTo(s2, 0);
        Assert.Empty(s1.Children);
        Assert.Equal(new[] { a, s2.Children[1] }, s2.Children);
        Assert.Same(s2, a.Parent);
        Assert.Equal(0, a.IndexInParent);

        Assert.Throws<InvalidOperationException>(() => doc.Tab.MoveTo(panel, 0));   // root
        Assert.Throws<InvalidOperationException>(() => panel.MoveTo(s1, 0));        // into own descendant
        Assert.Throws<InvalidOperationException>(() => s1.MoveTo(s1, 0));           // into itself
    }

    // ---- view model rules --------------------------------------------------------------

    [Fact]
    public void Rules_FollowTheSchema_AndContainerLimits()
    {
        Sta(() =>
        {
            var (_, _, doc) = Blank();
            var tab = doc.Root;
            var panel = tab.Children[0];
            var stack = panel.Children[0];
            var button = stack.Children[0];
            var pane = tab.AddChild(ElementKind.DockablePane);
            var panel2 = tab.AddChild(ElementKind.Panel);
            var full = panel2.AddChild(ElementKind.StackedItems);
            full.AddChild(ElementKind.Button); full.AddChild(ElementKind.Button); full.AddChild(ElementKind.Button);

            Assert.NotNull(tab.MoveBlockedReason(panel, 0));                 // the tab never moves
            Assert.NotNull(button.MoveBlockedReason(panel, 0));              // button directly in a panel
            Assert.NotNull(button.MoveBlockedReason(full, 0));               // 4th stacked item
            Assert.NotNull(pane.MoveBlockedReason(panel, 0));                // pane inside a panel
            Assert.NotNull(panel.MoveBlockedReason(stack, 0));               // into its own descendant
            Assert.NotNull(stack.MoveBlockedReason(stack, 0));               // into itself
            Assert.Null(stack.MoveBlockedReason(panel2, 0));                 // a stack into another panel is fine
            Assert.Null(button.MoveBlockedReason(stack, 0));                 // same parent reorder is always allowed
            Assert.Null(pane.MoveBlockedReason(tab, 0));                     // panes reorder among tab children
            Assert.Null(panel2.MoveBlockedReason(tab, 0));
            Assert.Contains("maximum of 3", button.MoveBlockedReason(full, 0));
            Assert.Contains("cannot go inside a panel", button.MoveBlockedReason(panel, 0));
        });
    }

    [Fact]
    public void MoveTo_AcrossParents_KeepsModelAndTreeInSync_AndRefreshesState()
    {
        Sta(() =>
        {
            var (_, editor, doc) = Blank();
            var panel = doc.Root.Children[0];
            var stack = panel.Children[0];
            var button = stack.Children[0];
            var stack2 = panel.AddChild(ElementKind.StackedItems);
            var other = stack2.AddChild(ElementKind.Button);
            Assert.True(other.IsLargeInPreview);
            doc.IsDirty = false;
            doc.Root.IsSelected = true;

            Assert.True(button.MoveTo(stack2, 0));

            Assert.Empty(stack.Children);
            Assert.Empty(stack.Node.Children);
            Assert.Equal(new[] { button, other }, stack2.Children);
            Assert.Equal(new[] { button.Node, other.Node }, stack2.Node.Children);
            Assert.Same(stack2, button.Parent);
            Assert.Same(stack2.Node, button.Node.Parent);
            Assert.True(doc.IsDirty);
            Assert.Same(button, editor.SelectedNode);
            Assert.True(stack2.IsExpanded);

            // Two items in the stack now: both render small; the stack header counts changed.
            Assert.False(button.IsLargeInPreview);
            Assert.False(other.IsLargeInPreview);
            Assert.Equal("Stacked items (2/3)", stack2.Header);
            Assert.Equal("Stacked items (0/3)", stack.Header);
            Assert.True(button.MoveDownCommand.CanExecute(null));
            Assert.False(button.MoveUpCommand.CanExecute(null));

            // Moving into a disabled container disables through the parent.
            stack.IsDisabled = true;
            Assert.True(other.MoveTo(stack, 0));
            Assert.True(other.IsEffectivelyDisabled);
            Assert.False(other.CanToggleEnabled);
            Assert.True(button.IsLargeInPreview); // alone again in stack2
        });
    }

    [Fact]
    public void MoveTo_WithinParent_ReordersTree_AndNoOpsWhenDroppedInPlace()
    {
        Sta(() =>
        {
            var (_, _, doc) = Blank();
            var panel = doc.Root.Children[0];
            var s1 = panel.Children[0];
            var s2 = panel.AddChild(ElementKind.StackedItems);
            var s3 = panel.AddChild(ElementKind.StackedItems);
            doc.IsDirty = false;

            Assert.False(s2.MoveTo(panel, 1)); // "before itself"
            Assert.False(s2.MoveTo(panel, 2)); // "after itself"
            Assert.False(doc.IsDirty);

            Assert.True(s1.MoveTo(panel, 3));  // to the end
            Assert.Equal(new[] { s2, s3, s1 }, panel.Children);
            Assert.Equal(new[] { s2.Node, s3.Node, s1.Node }, panel.Node.Children);
            Assert.True(doc.IsDirty);

            Assert.True(s1.MoveTo(panel, 0));
            Assert.Equal(new[] { s1, s2, s3 }, panel.Children);

            // Panes and panels reorder among the tab's children.
            var pane = doc.Root.AddChild(ElementKind.DockablePane);
            Assert.Equal(1, pane.Node.IndexInParent);
            Assert.True(pane.MoveTo(doc.Root, 0));
            Assert.Equal(new[] { pane, panel }, doc.Root.Children);
            Assert.Equal(ElementKind.DockablePane, doc.Model.Tab.Children[0].Kind);

            // The written file reflects the new order.
            var xml = RibbonXmlWriter.ToXmlString(doc.Model);
            Assert.True(xml.IndexOf("<dockablepane", StringComparison.Ordinal) < xml.IndexOf("<panel", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MoveTo_IllegalMove_ReturnsFalse_AndChangesNothing()
    {
        Sta(() =>
        {
            var (_, _, doc) = Blank();
            var panel = doc.Root.Children[0];
            var stack = panel.Children[0];
            var button = stack.Children[0];
            doc.IsDirty = false;

            Assert.False(button.MoveTo(panel, 0));
            Assert.Same(stack, button.Parent);
            Assert.Same(stack.Node, button.Node.Parent);
            Assert.Single(stack.Children);
            Assert.False(doc.IsDirty);
        });
    }
}
