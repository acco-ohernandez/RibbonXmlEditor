using RibbonXmlEditor.Models;
using RibbonXmlEditor.Services;
using Xunit;

namespace RibbonXmlEditor.Tests;

public class ValidatorTests
{
    // An Images folder that certainly exists, so missing-image checks are not downgraded.
    private static readonly ValidationContext Ctx = new(null, null, Path.GetTempPath());

    private static RibbonDocument Doc(string panelInner, string panelName = "P")
        => RibbonXmlReader.Parse($"<tab name=\"T\"><panel name=\"{panelName}\">{panelInner}</panel></tab>").Document;

    private static string Btn(string name, string classname = "Ns.Cmd_X", string text = "t",
        string image = "", string largeimage = "", string tooltipimage = "", string contexthelp = "")
        => $"<button name=\"{name}\" classname=\"{classname}\" text=\"{text}\" tooltip=\"\" image=\"{image}\" " +
           $"largeimage=\"{largeimage}\" tooltipimage=\"{tooltipimage}\" contexthelp=\"{contexthelp}\"/>";

    private static List<Issue> Validate(RibbonDocument doc, ValidationContext? ctx = null) => RibbonValidator.Validate(doc, ctx ?? Ctx);

    private static IEnumerable<string> Rules(IEnumerable<Issue> issues) => issues.Select(i => i.RuleId);

    [Fact]
    public void CleanDocument_HasNoIssues()
    {
        var doc = Doc($"<stackeditems>{Btn("a")}</stackeditems>");
        Assert.Empty(Validate(doc));
    }

    [Fact]
    public void RequiredAttributeEmpty_IsError()
    {
        var doc = Doc($"<stackeditems>{Btn("", classname: "", text: "")}</stackeditems>");
        var issues = Validate(doc).Where(i => i.RuleId == "required").ToList();
        Assert.Equal(3, issues.Count);
        Assert.All(issues, i => Assert.True(i.IsError));
        Assert.Equal(new[] { "name", "classname", "text" }, issues.Select(i => i.AttributeName));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void StackedItemsCount_OutOfRange_IsError(int count)
    {
        var inner = string.Concat(Enumerable.Range(1, count).Select(i => Btn($"b{i}")));
        var doc = Doc($"<stackeditems>{inner}</stackeditems>");
        var issue = Assert.Single(Validate(doc), i => i.RuleId == "stacked.count");
        Assert.True(issue.IsError);
        Assert.Contains("panel \"P\"", issue.Message);
    }

    [Fact]
    public void DisallowedChild_IsError()
    {
        var doc = Doc(Btn("loose"));
        var issue = Assert.Single(Validate(doc), i => i.RuleId == "child.notallowed");
        Assert.True(issue.IsError);
        Assert.Equal("loose", issue.Node!.Name);
    }

    [Fact]
    public void RelativeImagePath_IsError()
    {
        var doc = Doc($"<stackeditems>{Btn("a", image: "Images\\A_16x16.png")}</stackeditems>");
        Assert.Contains("image.relative", Rules(Validate(doc)));
    }

    [Fact]
    public void MissingImage_IsError_ButOnlyWarning_WhenImagesFolderAbsent()
    {
        const string imagesFolder = @"C:\ThisFolderDoesNotExist\Images";
        var doc = Doc($"<stackeditems>{Btn("a", image: imagesFolder + @"\A_16x16.png")}</stackeditems>");

        var strict = Validate(doc, Ctx);
        Assert.Contains(strict, i => i.RuleId == "image.missing" && i.IsError);

        var lenient = Validate(doc, new ValidationContext(null, null, imagesFolder));
        Assert.DoesNotContain("image.missing", Rules(lenient));
        Assert.Contains(lenient, i => i.RuleId == "image.unverified" && !i.IsError);
    }

    [Fact]
    public void ImageSizeConvention_IsWarning()
    {
        var doc = Doc($"<stackeditems>{Btn("a", image: @"C:\x\A_32x32.png", largeimage: @"C:\x\A_16x16.png", tooltipimage: @"C:\x\A_32x32.png")}</stackeditems>");
        var sizes = Validate(doc).Where(i => i.RuleId == "image.size").ToList();
        Assert.Equal(3, sizes.Count);
        Assert.All(sizes, i => Assert.False(i.IsError));
    }

    [Fact]
    public void PulldownImage_Expects32()
    {
        var doc = Doc($"<stackeditems><pulldownbuttons name=\"pd\" image=\"C:\\x\\A_16x16.png\">{Btn("a")}</pulldownbuttons></stackeditems>");
        Assert.Contains(Validate(doc), i => i.RuleId == "image.size" && i.Message.Contains("32x32"));
    }

    [Fact]
    public void BadHelpUrl_IsError()
    {
        var doc = Doc($"<stackeditems>{Btn("a", contexthelp: "www.google.com")}</stackeditems>");
        Assert.Contains(Validate(doc), i => i.RuleId == "help.url" && i.IsError);

        var ok = Doc($"<stackeditems>{Btn("a", contexthelp: "https://www.google.com")}</stackeditems>");
        Assert.DoesNotContain("help.url", Rules(Validate(ok)));
    }

    [Fact]
    public void DuplicateNamesInPanel_AcrossStacks_IsError()
    {
        var doc = Doc($"<stackeditems>{Btn("same")}</stackeditems><stackeditems>{Btn("same")}</stackeditems>");
        var dups = Validate(doc).Where(i => i.RuleId == "name.duplicate").ToList();
        Assert.Equal(2, dups.Count);
        Assert.All(dups, i => Assert.True(i.IsError));
    }

    [Fact]
    public void SameNameInDifferentPanels_IsFine()
    {
        var doc = RibbonXmlReader.Parse(
            $"<tab name=\"T\"><panel name=\"A\"><stackeditems>{Btn("x")}</stackeditems></panel>" +
            $"<panel name=\"B\"><stackeditems>{Btn("x")}</stackeditems></panel></tab>").Document;
        Assert.DoesNotContain("name.duplicate", Rules(Validate(doc)));
    }

    [Fact]
    public void DuplicatePanelNames_IsError()
    {
        var doc = RibbonXmlReader.Parse(
            $"<tab name=\"T\"><panel name=\"A\"><stackeditems>{Btn("x")}</stackeditems></panel>" +
            $"<panel name=\"A\"><stackeditems>{Btn("y")}</stackeditems></panel></tab>").Document;
        Assert.Equal(2, Validate(doc).Count(i => i.RuleId == "name.duplicate"));
    }

    [Fact]
    public void TwoSplitButtons_IsError()
    {
        var doc = Doc($"<splitbuttons>{Btn("a")}</splitbuttons><splitbuttons>{Btn("b")}</splitbuttons>");
        var issue = Assert.Single(Validate(doc), i => i.RuleId == "split.multiple");
        Assert.True(issue.IsError);
    }

    [Fact]
    public void ClassName_FormatAndUnknown_AreWarnings()
    {
        var bad = Doc($"<stackeditems>{Btn("a", classname: "no namespace here")}</stackeditems>");
        Assert.Contains(Validate(bad), i => i.RuleId == "classname.format" && !i.IsError);

        var known = new HashSet<string> { "Ns.Cmd_Known" };
        var ctx = new ValidationContext(known, "Tab.dll", Path.GetTempPath());
        var unknown = Doc($"<stackeditems>{Btn("a", classname: "Ns.Cmd_Other")}</stackeditems>");
        Assert.Contains(Validate(unknown, ctx), i => i.RuleId == "classname.unknown" && i.Message.Contains("Tab.dll"));

        var fine = Doc($"<stackeditems>{Btn("a", classname: "Ns.Cmd_Known")}</stackeditems>");
        Assert.Empty(Validate(fine, ctx));
    }

    [Fact]
    public void ShowImage_OddValue_IsWarning()
    {
        var doc = Doc("<stackeditems><textbox name=\"tb\" prompttext=\"\" longdescription=\"\" image=\"\" showimage=\"yes\" tooltip=\"\" tooltipimage=\"\"/></stackeditems>");
        Assert.Contains(Validate(doc), i => i.RuleId == "flag.value" && !i.IsError);
    }

    [Fact]
    public void EmptyContainer_IsWarning()
    {
        var doc = Doc("<stackeditems><pulldownbuttons name=\"pd\" image=\"C:\\x\\A_32x32.png\"/></stackeditems>");
        Assert.Contains(Validate(doc), i => i.RuleId == "container.empty" && !i.IsError);
    }

    [Fact]
    public void SaveTarget_WarnsAboutSecondRibbonInFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RibbonXmlEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Other.ribbon"), "<tab name=\"x\"/>");
            var target = Path.Combine(dir, "Mine.ribbon");
            var issue = Assert.Single(RibbonValidator.CheckSaveTarget(target));
            Assert.Equal("save.multiple-ribbons", issue.RuleId);
            Assert.Contains("Other.ribbon", issue.Message);

            // Overwriting the only ribbon in the folder is fine.
            Assert.Empty(RibbonValidator.CheckSaveTarget(Path.Combine(dir, "Other.ribbon")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ProductionMechanicalFile_HasNoErrors()
    {
        if (!File.Exists(TestPaths.Mechanical))
            return;
        var doc = RibbonXmlReader.Load(TestPaths.Mechanical).Document;
        var issues = Validate(doc, new ValidationContext(null, null, Path.Combine(TestPaths.DeployedRoot, "Images")));
        Assert.DoesNotContain(issues, i => i.IsError);
    }
}
