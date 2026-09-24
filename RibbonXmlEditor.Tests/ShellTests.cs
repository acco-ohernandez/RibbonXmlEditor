using RibbonXmlEditor.ViewModels;
using Xunit;
using static RibbonXmlEditor.Tests.ViewModelTests;

namespace RibbonXmlEditor.Tests;

/// <summary>Multi-document shell: tabs, activation, closing, Save All.</summary>
public class ShellTests
{
    private const string ValidRibbon =
        "<tab name=\"T\"><panel name=\"P\"><stackeditems>" +
        "<button name=\"b\" classname=\"N.C\" text=\"t\" tooltip=\"\" image=\"\" largeimage=\"\" tooltipimage=\"\" contexthelp=\"\"/>" +
        "</stackeditems></panel></tab>";

    /// <summary>A complete, valid ribbon in its own folder (so rule 17 never prompts). Returns the file path.</summary>
    private static string WriteRibbon(string name = "T.ribbon")
    {
        var dir = Path.Combine(Path.GetTempPath(), "RibbonXmlEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, ValidRibbon);
        return path;
    }

    private static void Cleanup(params string[] files)
    {
        foreach (var f in files)
        {
            var dir = Path.GetDirectoryName(f)!;
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Startup_HasNoDocuments_AndPlainTitle()
    {
        Sta(() =>
        {
            var main = new MainViewModel();
            Assert.Empty(main.Documents);
            Assert.Null(main.ActiveDocument);
            Assert.False(main.HasDocuments);
            Assert.Equal("Ribbon XML Editor", main.Title);
            Assert.False(main.CloseActiveCommand.CanExecute(null));
            Assert.False(main.SaveCommand.CanExecute(null));
            Assert.False(main.SaveAllCommand.CanExecute(null));
            Assert.False(main.CloseAllCommand.CanExecute(null));
            Assert.True(main.CanClose());
        });
    }

    [Fact]
    public void OpenFile_Twice_ActivatesExistingTab()
    {
        Sta(() =>
        {
            var path = WriteRibbon();
            try
            {
                var main = new MainViewModel();
                main.OpenFile(path);
                var fileTab = main.ActiveDocument!;
                Assert.False(fileTab.Document.IsDirty);

                main.NewCommand.Execute(null);
                Assert.Equal(2, main.Documents.Count);
                Assert.NotSame(fileTab, main.ActiveDocument);

                main.OpenFile(path.ToUpperInvariant()); // same file, different casing

                Assert.Equal(2, main.Documents.Count);
                Assert.Same(fileTab, main.ActiveDocument);
            }
            finally
            {
                Cleanup(path);
            }
        });
    }

    [Fact]
    public void CloseTab_ActivatesLeftNeighbour_ElseRight()
    {
        Sta(() =>
        {
            var main = new MainViewModel();
            main.NewCommand.Execute(null);
            main.NewCommand.Execute(null);
            main.NewCommand.Execute(null);
            var (first, middle, last) = (main.Documents[0], main.Documents[1], main.Documents[2]);

            main.ActiveDocument = middle;
            Assert.True(main.CloseTab(middle));
            Assert.Same(first, main.ActiveDocument);            // left neighbour
            Assert.Equal(new[] { first, last }, main.Documents);

            Assert.True(main.CloseTab(first));
            Assert.Same(last, main.ActiveDocument);             // nothing on the left -> right
            Assert.Single(main.Documents);

            // Closing a non-active tab leaves the active one alone.
            main.NewCommand.Execute(null);
            var extra = main.ActiveDocument!;
            Assert.True(main.CloseTab(last));
            Assert.Same(extra, main.ActiveDocument);
        });
    }

    [Fact]
    public void CloseAll_AndCloseOthers()
    {
        Sta(() =>
        {
            var main = new MainViewModel();
            main.NewCommand.Execute(null);
            main.NewCommand.Execute(null);
            main.NewCommand.Execute(null);
            var keep = main.Documents[1];

            main.CloseOthersCommand.Execute(keep);
            Assert.Single(main.Documents);
            Assert.Same(keep, main.ActiveDocument);

            main.NewCommand.Execute(null);
            main.CloseAllCommand.Execute(null);
            Assert.Empty(main.Documents);
            Assert.Null(main.ActiveDocument);
            Assert.False(main.HasDocuments);
        });
    }

    [Fact]
    public void SaveAll_TouchesOnlyDirtyDocs()
    {
        Sta(() =>
        {
            var path1 = WriteRibbon("One.ribbon");
            var path2 = WriteRibbon("Two.ribbon");
            try
            {
                var main = new MainViewModel();
                main.OpenFile(path1);
                main.OpenFile(path2);
                var (ed1, ed2) = (main.Documents[0], main.Documents[1]);
                Assert.False(ed1.Document.IsDirty);
                Assert.False(ed2.Document.IsDirty);

                var before1 = File.ReadAllText(path1);
                ed2.Document.Root.GetField("name")!.Value = "Renamed";
                Assert.True(ed2.Document.IsDirty);

                main.SaveAllCommand.Execute(null);

                Assert.Equal(before1, File.ReadAllText(path1));          // untouched
                Assert.Contains("name=\"Renamed\"", File.ReadAllText(path2));
                Assert.False(ed2.Document.IsDirty);
                Assert.Equal("Two.ribbon", ed2.Header);
                Assert.True(main.CanClose());                           // nothing dirty -> no prompts
            }
            finally
            {
                Cleanup(path1, path2);
            }
        });
    }

    [Fact]
    public void RevalidateAll_RepopulatesEveryTab()
    {
        Sta(() =>
        {
            var main = new MainViewModel();
            main.NewCommand.Execute(null);
            main.NewCommand.Execute(null);
            foreach (var d in main.Documents)
                d.Issues.Clear();

            main.RevalidateAll();

            Assert.All(main.Documents, d => Assert.Single(d.Issues));
        });
    }

    [Fact]
    public void ActivatingTab_UpdatesTitle_AndActiveCommands()
    {
        Sta(() =>
        {
            var path = WriteRibbon("Named.ribbon");
            try
            {
                var main = new MainViewModel();
                main.NewCommand.Execute(null);
                var blank = main.ActiveDocument!;
                main.OpenFile(path);
                Assert.Equal("Named.ribbon - Ribbon XML Editor", main.Title);

                main.ActiveDocument = blank;
                Assert.Equal("Untitled.ribbon - Ribbon XML Editor", main.Title);
                Assert.True(main.SaveCommand.CanExecute(null));
                Assert.False(main.RescanDllCommand.CanExecute(null)); // no DLL on a blank doc

                blank.Document.Root.GetField("name")!.Value = "X";      // dirty -> header star -> title
                Assert.Equal("Untitled.ribbon* - Ribbon XML Editor", main.Title);
            }
            finally
            {
                Cleanup(path);
            }
        });
    }
}
