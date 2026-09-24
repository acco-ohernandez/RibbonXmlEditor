using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.Services;
using RibbonXmlEditor.ViewModels;
using RibbonXmlEditor.ViewModels.Fields;
using Xunit;

namespace RibbonXmlEditor.Tests;

/// <summary>Headless checks of the tree commands and field bindings. Runs on an STA thread like WPF would.</summary>
public class ViewModelTests
{
    internal static void Sta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null)
            throw new Xunit.Sdk.XunitException($"Failed on STA thread: {error}");
    }

    internal static (MainViewModel main, DocumentViewModel doc) Blank()
    {
        var main = new MainViewModel();
        return (main, main.Document!);
    }

    [Fact]
    public void NewDocument_StartsClean_WithTabSelected()
    {
        Sta(() =>
        {
            var (main, doc) = Blank();
            Assert.False(doc.IsDirty);
            Assert.Same(doc.Root, main.SelectedNode);
            Assert.Equal(ElementKind.Tab, doc.Root.Node.Kind);
            // The starter button has no command class yet, so exactly one required-field error is expected.
            var only = Assert.Single(main.Issues);
            Assert.Equal("required", only.Issue.RuleId);
            Assert.Equal("classname", only.AttributeName);
        });
    }

    [Fact]
    public void AddChild_UpdatesModelAndTree_SelectsNew_AndDisablesAtLimit()
    {
        Sta(() =>
        {
            var (main, doc) = Blank();
            var stack = doc.Root.Children[0].Children[0]; // tab / panel / stackeditems (1 button)
            Assert.Equal(ElementKind.StackedItems, stack.Node.Kind);
            var addButton = stack.AddChildOptions.Single(o => o.Kind == ElementKind.Button);

            Assert.True(addButton.IsEnabled);
            var b2 = stack.AddChild(ElementKind.Button);
            var b3 = stack.AddChild(ElementKind.Button);

            Assert.Equal(3, stack.Node.Children.Count);
            Assert.Equal(3, stack.Children.Count);
            Assert.Same(b3, main.SelectedNode);
            Assert.True(doc.IsDirty);
            Assert.False(addButton.IsEnabled);           // 3 = max for stacked items
            Assert.False(addButton.Command.CanExecute(null));
            Assert.Equal("Stacked items (3/3)", stack.Header);
        });
    }

    [Fact]
    public void Duplicate_ClonesAfterOriginal_WithCopySuffix()
    {
        Sta(() =>
        {
            var (_, doc) = Blank();
            var panel = doc.Root.Children[0];
            var stack = panel.Children[0];
            var button = stack.Children[0];
            button.GetField("name")!.Value = "About";

            button.DuplicateCommand.Execute(null);

            Assert.Equal(2, stack.Children.Count);
            Assert.Equal("About Copy", stack.Children[1].Node.Name);
            Assert.Equal("About Copy", stack.Node.Children[1].Name);
            Assert.Same(stack.Node, stack.Node.Children[1].Parent);
        });
    }

    [Fact]
    public void MoveUpDown_ReordersModelAndTree()
    {
        Sta(() =>
        {
            var (_, doc) = Blank();
            var panel = doc.Root.Children[0];
            var second = panel.AddChild(ElementKind.Separator);
            Assert.Equal(1, second.Node.IndexInParent);
            Assert.True(second.MoveUpCommand.CanExecute(null));
            Assert.False(second.MoveDownCommand.CanExecute(null));

            second.MoveUpCommand.Execute(null);

            Assert.Equal(0, second.Node.IndexInParent);
            Assert.Same(second, panel.Children[0]);
            Assert.False(second.MoveUpCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Delete_ChildlessNode_RemovesAndSelectsNeighbour()
    {
        Sta(() =>
        {
            var (main, doc) = Blank();
            var panel = doc.Root.Children[0];
            var sep = panel.AddChild(ElementKind.Separator);
            Assert.Equal(2, panel.Children.Count);

            sep.DeleteCommand.Execute(null); // no children -> no confirmation dialog

            Assert.Single(panel.Children);
            Assert.Single(panel.Node.Children);
            Assert.Same(panel.Children[0], main.SelectedNode);
            Assert.False(doc.Root.DeleteCommand.CanExecute(null)); // the tab cannot be deleted
        });
    }

    [Fact]
    public void FieldValue_NormalisesWindowsLineBreaks_AndMarksDirty()
    {
        Sta(() =>
        {
            var (_, doc) = Blank();
            var button = doc.Root.Children[0].Children[0].Children[0];
            var text = (MultilineTextField)button.GetField("text")!;

            text.Value = "Create\r\nCatalog Page";

            Assert.Equal("Create\nCatalog Page", button.Node["text"]);
            Assert.True(doc.IsDirty);
            Assert.Equal("Button: Button1", button.Header);
        });
    }

    [Fact]
    public void TrueOrEmptyField_MapsToLiteralTrue()
    {
        Sta(() =>
        {
            var (_, doc) = Blank();
            var stack = doc.Root.Children[0].Children[0];
            var textbox = stack.AddChild(ElementKind.TextBox);
            var flag = (TrueOrEmptyField)textbox.GetField("showimage")!;

            flag.IsChecked = true;
            Assert.Equal("true", textbox.Node["showimage"]);
            flag.IsChecked = false;
            Assert.Equal("", textbox.Node["showimage"]);
        });
    }

    [Fact]
    public void Revalidate_DistributesIssuesToFieldsAndRollsUpFlags()
    {
        Sta(() =>
        {
            var (main, doc) = Blank();
            var button = doc.Root.Children[0].Children[0].Children[0];
            button.GetField("classname")!.Value = "";                 // required -> error on field
            button.GetField("contexthelp")!.Value = "not a url";      // error on field

            main.Revalidate();

            Assert.Equal(2, main.ErrorCount);
            Assert.True(button.GetField("classname")!.HasError);
            Assert.True(button.GetField("contexthelp")!.HasError);
            Assert.False(button.GetField("name")!.HasIssues);
            Assert.True(button.HasError);
            Assert.True(button.HasOwnError);
            Assert.True(doc.Root.HasError);     // rolled up to the tab
            Assert.False(doc.Root.HasOwnError); // but the tab itself is fine
            Assert.All(main.Issues, i => Assert.Same(button, i.Node));
            Assert.Contains("@ classname", main.Issues.First(i => i.AttributeName == "classname").Location);

            button.GetField("classname")!.Value = "Ns.Cmd_X";
            button.GetField("contexthelp")!.Value = "";
            main.Revalidate();
            Assert.Empty(main.Issues);
            Assert.False(doc.Root.HasError);
        });
    }

    [Fact]
    public void OpenFile_LoadsTree_AndReportsLoadRepairs()
    {
        Sta(() =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "RibbonXmlEditorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "T.ribbon");
            try
            {
                File.WriteAllText(path,
                    "<tab name=\"T\"><panel name=\"P\"><stackeditems><button name=\"b\" classname=\"N.C\" text=\"t\"/></stackeditems></panel></tab>");

                var (main, _) = Blank();
                main.OpenFile(path);

                var doc = main.Document!;
                Assert.Equal(path, doc.FilePath);
                Assert.True(doc.IsDirty);                       // missing attributes must be written back
                Assert.Equal("Tab: T", doc.Root.Header);
                Assert.Contains(main.Issues, i => i.Issue.RuleId == "load.missing-attribute");
                Assert.Equal(0, main.ErrorCount);               // repairs are warnings; the doc itself is valid
                Assert.False(main.IsDeployedCopy);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        });
    }

    [Fact]
    public void Template_LoadsEveryElementKind()
    {
        var result = TemplateProvider.LoadButtonStructureTemplate();
        var kinds = result.Document.Tab.DescendantsAndSelf().Select(n => n.Kind).Distinct().ToHashSet();
        foreach (var def in RibbonSchema.All.Where(d => d.Kind != ElementKind.Separator))
            Assert.Contains(def.Kind, kinds);
        Assert.Empty(result.Issues);
    }
}
