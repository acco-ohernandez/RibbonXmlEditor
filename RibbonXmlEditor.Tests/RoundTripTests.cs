using System.Text;
using RibbonXmlEditor.Models;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.Services;
using Xunit;

namespace RibbonXmlEditor.Tests;

public class RoundTripTests
{
    public static IEnumerable<object[]> ProductionFiles()
        => TestPaths.ProductionRibbons.Select(p => new object[] { Path.Combine(TestPaths.RepoRoot, p) });

    [Theory]
    [MemberData(nameof(ProductionFiles))]
    public void ProductionFile_RoundTrips_StructurallyIdentical(string path)
    {
        if (!File.Exists(path))
            return; // repo not present on this machine

        var first = RibbonXmlReader.Load(path);
        Assert.Empty(first.Issues); // production files are clean
        Assert.False(first.NeedsRepairSave);

        var bytes = RibbonXmlWriter.ToBytes(first.Document);
        var second = RibbonXmlReader.Parse(Encoding.UTF8.GetString(bytes));

        Assert.Empty(second.Issues);
        AssertDocumentsEqual(first.Document, second.Document);

        // Writing twice yields byte-identical output.
        Assert.Equal(bytes, RibbonXmlWriter.ToBytes(second.Document));
    }

    [Fact]
    public void Mechanical_ConnectTo_CaptionKeepsLineBreak()
    {
        if (!File.Exists(TestPaths.Mechanical))
            return;

        var doc = RibbonXmlReader.Load(TestPaths.Mechanical).Document;
        var btn = doc.Tab.Descendants().Single(n => n.Kind == ElementKind.Button && n.Name == "btnConnectTo");
        Assert.Equal("Connect\nTwo Elements", btn["text"]);

        var again = RibbonXmlReader.Parse(RibbonXmlWriter.ToXmlString(doc)).Document;
        var btn2 = again.Tab.Descendants().Single(n => n.Kind == ElementKind.Button && n.Name == "btnConnectTo");
        Assert.Equal("Connect\nTwo Elements", btn2["text"]);
    }

    [Fact]
    public void Mechanical_HeaderIsParsed()
    {
        if (!File.Exists(TestPaths.Mechanical))
            return;

        var doc = RibbonXmlReader.Load(TestPaths.Mechanical).Document;
        Assert.Equal("2.2.0", doc.Version);
        Assert.Equal(new DateOnly(2026, 7, 14), doc.VersionDate);
        Assert.Single(doc.OtherLeadingComments);
        Assert.Contains("Copied from", doc.OtherLeadingComments[0]);
        Assert.Equal(1, doc.VersionCommentPosition); // "Copied from" first, then Version
        Assert.Equal("ENG Mechanical", doc.Tab.Name);
        // Since 2026-10-09: the ACCODocs dockable pane first, then the four panels.
        Assert.Equal(5, doc.Tab.Children.Count);
        Assert.Equal(ElementKind.DockablePane, doc.Tab.Children[0].Kind);
        Assert.Equal("ACCODocsLibrary", doc.Tab.Children[0].Name);
        Assert.Equal(4, doc.Tab.Children.Count(c => c.Kind == ElementKind.Panel));
    }

    [Fact]
    public void Output_HasNoBom_UsesCrlf_DeclaresUtf8()
    {
        var doc = RibbonDocument.CreateBlank("T");
        doc.Tab.Children[0].Children[0].Children[0]["tooltip"] = "line one\nline two";

        var bytes = RibbonXmlWriter.ToBytes(doc);
        Assert.Equal((byte)'<', bytes[0]);

        var text = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text);
        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "")); // no lone LF anywhere
        Assert.Contains("tooltip=\"line one\r\nline two\"", text); // raw line break inside the attribute
    }

    [Fact]
    public void Writer_EmitsEveryAttributeAndCommentsInOrder()
    {
        var doc = RibbonDocument.CreateBlank("T");
        doc.OtherLeadingComments.Add(" Copied from: somewhere ");
        doc.VersionCommentPosition = 1;
        doc.Version = "3.0.0";
        doc.VersionDate = new DateOnly(2026, 9, 23);

        var text = RibbonXmlWriter.ToXmlString(doc);
        int copied = text.IndexOf("<!-- Copied from: somewhere -->", StringComparison.Ordinal);
        int version = text.IndexOf("<!-- Version 3.0.0 2026-09-23 -->", StringComparison.Ordinal);
        Assert.True(copied >= 0 && version > copied, text);

        foreach (var a in RibbonSchema.ByKind(ElementKind.Button).Attributes)
            Assert.Contains($"{a.XmlName}=\"", text);
        Assert.Contains("<separator", RibbonXmlWriter.ToXmlString(WithSeparator()));
    }

    [Fact]
    public void MissingAttribute_IsReportedAndRepairedOnSave()
    {
        const string xml = "<tab name=\"T\"><panel name=\"P\"><stackeditems>" +
                           "<button name=\"b\" classname=\"c\" text=\"t\"/></stackeditems></panel></tab>";
        var result = RibbonXmlReader.Parse(xml);

        Assert.True(result.NeedsRepairSave);
        var missing = result.Issues.Where(i => i.RuleId == "load.missing-attribute").Select(i => i.AttributeName).ToList();
        Assert.Equal(new[] { "tooltip", "image", "largeimage", "tooltipimage", "contexthelp" }, missing);

        var written = RibbonXmlWriter.ToXmlString(result.Document);
        Assert.Contains("tooltip=\"\"", written);
        Assert.Empty(RibbonXmlReader.Parse(written).Issues);
    }

    [Fact]
    public void CommentInsideContainer_IsDroppedWithWarning()
    {
        const string xml = "<tab name=\"T\"><panel name=\"P\"><stackeditems><!-- hi -->" +
                           "<button name=\"b\" classname=\"c\" text=\"t\" tooltip=\"\" image=\"\" largeimage=\"\" tooltipimage=\"\" contexthelp=\"\"/>" +
                           "</stackeditems></panel></tab>";
        var result = RibbonXmlReader.Parse(xml);
        Assert.Contains(result.Issues, i => i.RuleId == "load.comment-dropped");
        Assert.True(result.NeedsRepairSave);
        Assert.DoesNotContain("<!--", RibbonXmlWriter.ToXmlString(result.Document).Split("<tab")[1]);
    }

    [Fact]
    public void UnknownAttribute_IsKeptAndWarned()
    {
        const string xml = "<tab name=\"T\" extra=\"x\"></tab>";
        var result = RibbonXmlReader.Parse(xml);
        Assert.Contains(result.Issues, i => i.RuleId == "load.unknown-attribute");
        Assert.Contains("extra=\"x\"", RibbonXmlWriter.ToXmlString(result.Document));
    }

    [Fact]
    public void DisallowedKnownChild_IsKeptForValidator()
    {
        const string xml = "<tab name=\"T\"><panel name=\"P\">" +
                           "<button name=\"b\" classname=\"c\" text=\"t\" tooltip=\"\" image=\"\" largeimage=\"\" tooltipimage=\"\" contexthelp=\"\"/>" +
                           "</panel></tab>";
        var doc = RibbonXmlReader.Parse(xml).Document;
        Assert.Equal(ElementKind.Button, doc.Tab.Children[0].Children[0].Kind);
    }

    [Fact]
    public void RootMustBeTab()
    {
        Assert.Throws<InvalidDataException>(() => RibbonXmlReader.Parse("<ribbon><tab name=\"T\"/></ribbon>"));
    }

    [Fact]
    public void Save_WritesAtomicallyAndSetsFilePath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "RibbonXmlEditorTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "Test.ribbon");
        try
        {
            var doc = RibbonDocument.CreateBlank("T");
            RibbonXmlWriter.Save(doc, path);
            Assert.Equal(path, doc.FilePath);
            Assert.Single(Directory.GetFiles(dir)); // no temp file left behind
            Assert.Equal("T", RibbonXmlReader.Load(path).Document.Tab.Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ---- helpers -------------------------------------------------------------------------

    private static RibbonDocument WithSeparator()
    {
        var doc = RibbonDocument.CreateBlank("T");
        doc.Tab.Children[0].AddChild(new RibbonNode(ElementKind.Separator));
        return doc;
    }

    internal static void AssertDocumentsEqual(RibbonDocument a, RibbonDocument b)
    {
        Assert.Equal(a.Version, b.Version);
        Assert.Equal(a.VersionDate, b.VersionDate);
        Assert.Equal(a.OtherLeadingComments, b.OtherLeadingComments);
        Assert.Equal(Math.Min(a.VersionCommentPosition, a.OtherLeadingComments.Count),
                     Math.Min(b.VersionCommentPosition, b.OtherLeadingComments.Count));
        AssertNodesEqual(a.Tab, b.Tab);
    }

    private static void AssertNodesEqual(RibbonNode a, RibbonNode b)
    {
        Assert.Equal(a.Kind, b.Kind);
        foreach (var attr in a.Def.Attributes)
            Assert.True(a[attr.XmlName] == b[attr.XmlName], $"{a.PathText()} @{attr.XmlName}: '{a[attr.XmlName]}' vs '{b[attr.XmlName]}'");
        Assert.Equal(a.ExtraAttributes, b.ExtraAttributes);
        Assert.True(a.Children.Count == b.Children.Count, $"{a.PathText()} child count {a.Children.Count} vs {b.Children.Count}");
        for (int i = 0; i < a.Children.Count; i++)
            AssertNodesEqual(a.Children[i], b.Children[i]);
    }
}
