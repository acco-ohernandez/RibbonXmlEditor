using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RibbonXmlEditor.Schema;
using Xunit;
using static RibbonXmlEditor.Tests.ViewModelTests;

namespace RibbonXmlEditor.Tests;

/// <summary>Headless checks for the ribbon-preview state on NodeViewModel and closing the last tab.</summary>
public class PreviewViewModelTests
{
    private static List<string> Record(INotifyPropertyChanged source)
    {
        var names = new List<string>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName ?? string.Empty);
        return names;
    }

    [Fact]
    public void CloseTab_LastTab_EmptiesShell_AndNewWorksAfterwards()
    {
        Sta(() =>
        {
            var (main, editor, _) = Blank();
            Assert.True(main.CloseActiveCommand.CanExecute(null));

            editor.CloseCommand.Execute(null); // not dirty -> no prompt

            Assert.Null(main.ActiveDocument);
            Assert.Empty(main.Documents);
            Assert.False(main.HasDocuments);
            Assert.Equal("Ribbon XML Editor", main.Title);
            Assert.False(main.CloseActiveCommand.CanExecute(null));
            Assert.False(main.SaveCommand.CanExecute(null));
            Assert.False(main.SaveAllCommand.CanExecute(null));

            main.NewCommand.Execute(null);
            Assert.NotNull(main.ActiveDocument);
            Assert.Same(main.ActiveDocument!.Document.Root, main.ActiveDocument.SelectedNode);
        });
    }

    [Fact]
    public void SelectCommand_SelectsExpandsAncestors_AndUpdatesEditorSelection()
    {
        Sta(() =>
        {
            var (_, editor, doc) = Blank();
            var panel = doc.Root.Children[0];
            var stack = panel.Children[0];
            var button = stack.Children[0];
            panel.IsExpanded = false;
            stack.IsExpanded = false;

            button.SelectCommand.Execute(null);

            Assert.True(button.IsSelected);
            Assert.Same(button, editor.SelectedNode);
            Assert.True(panel.IsExpanded);
            Assert.True(stack.IsExpanded);
        });
    }

    [Fact]
    public void SelectCommand_OnPulldownChild_ClosesPopup()
    {
        Sta(() =>
        {
            var (_, _, doc) = Blank();
            var stack = doc.Root.Children[0].Children[0];
            var pulldown = stack.AddChild(ElementKind.PulldownButtons);
            var child = pulldown.AddChild(ElementKind.Button);

            var names = Record(pulldown);
            pulldown.IsPreviewExpanded = true;
            Assert.Contains("IsPreviewExpanded", names);

            child.SelectCommand.Execute(null);

            Assert.False(pulldown.IsPreviewExpanded);
            Assert.True(child.IsSelected);
        });
    }

    [Fact]
    public void IsLargeInPreview_FollowsStackedCount_AndFirstChildFollowsMoves()
    {
        Sta(() =>
        {
            var (_, _, doc) = Blank();
            var panel = doc.Root.Children[0];
            var stack = panel.Children[0];
            var first = stack.Children[0];
            Assert.True(first.IsLargeInPreview);

            var names = Record(first);
            var second = stack.AddChild(ElementKind.Button);
            Assert.False(first.IsLargeInPreview);
            Assert.False(second.IsLargeInPreview);
            Assert.Contains("IsLargeInPreview", names);

            second.DeleteCommand.Execute(null);
            Assert.True(first.IsLargeInPreview);

            var split = panel.AddChild(ElementKind.SplitButtons);
            var a = split.AddChild(ElementKind.Button);
            var b = split.AddChild(ElementKind.Button);
            Assert.True(a.IsLargeInPreview);
            Assert.Same(a, split.FirstChild);

            b.MoveUpCommand.Execute(null);
            Assert.Same(b, split.FirstChild);

            Assert.Null(panel.Slideout);
            var slideout = panel.AddChild(ElementKind.SlideoutPanel);
            Assert.Same(slideout, panel.Slideout);
        });
    }

    [Fact]
    public void Caption_AndImages_NotifyOnAttributeChange()
    {
        Sta(() =>
        {
            var (_, _, doc) = Blank();
            var stack = doc.Root.Children[0].Children[0];
            var button = stack.Children[0];

            var names = Record(button);
            button.GetField("text")!.Value = "Create\r\nCatalog";
            Assert.Contains("Caption", names);
            Assert.Equal("Create\nCatalog", button.Caption);
            Assert.Equal("Create Catalog", button.CaptionSingleLine);

            button.GetField("text")!.Value = "";
            Assert.Equal("Button1", button.Caption); // falls back to the name
            button.GetField("name")!.Value = "";
            Assert.Equal("(no caption)", button.Caption);

            names.Clear();
            button.GetField("image")!.Value = @"C:\definitely\missing.png";
            Assert.Contains("SmallImage", names);
            Assert.Null(button.SmallImage);
            Assert.False(button.HasSmallImage);

            var dir = Path.Combine(Path.GetTempPath(), "RibbonXmlEditorTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var png = Path.Combine(dir, "Tool_16x16.png");
                WritePng(png, 16);
                button.GetField("image")!.Value = png;
                Assert.True(button.HasSmallImage);
                Assert.Equal(16, button.SmallImage!.PixelWidth);
                Assert.Null(button.LargeImage); // largeimage still empty

                // Changing the path invalidates the cache.
                button.GetField("image")!.Value = "";
                Assert.False(button.HasSmallImage);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }

            var pulldown = stack.AddChild(ElementKind.PulldownButtons);
            pulldown.GetField("name")!.Value = "Export Lists";
            Assert.Equal("Export Lists", pulldown.Caption);

            var textbox = doc.Root.Children[0].AddChild(ElementKind.StackedItems).AddChild(ElementKind.TextBox);
            textbox.GetField("name")!.Value = "tb";
            Assert.Equal("tb", textbox.Caption);
            textbox.GetField("prompttext")!.Value = "Type here";
            Assert.Equal("Type here", textbox.Caption);
        });
    }

    [Fact]
    public void RevealInPreview_ExpandsCollapsedSlideout_WhenNodeSelected()
    {
        Sta(() =>
        {
            var (_, editor, doc) = Blank();
            var panel = doc.Root.Children[0];
            var slideout = panel.AddChild(ElementKind.SlideoutPanel);
            var button = slideout.AddChild(ElementKind.Button);
            slideout.IsPreviewExpanded = false;
            doc.Root.IsSelected = true; // move selection away first
            button.IsSelected = false;  // (the tree would have deselected it)

            button.IsSelected = true;   // as the tree would do

            Assert.Same(button, editor.SelectedNode);
            Assert.True(slideout.IsPreviewExpanded);

            doc.ClosePreviewPopups();   // only drop lists close, not slide-outs
            Assert.True(slideout.IsPreviewExpanded);
        });
    }

    private static void WritePng(string path, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawRectangle(Brushes.SeaGreen, null, new System.Windows.Rect(0, 0, size, size));
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}
