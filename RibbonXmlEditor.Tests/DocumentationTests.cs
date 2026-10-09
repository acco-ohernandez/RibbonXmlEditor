using System.Windows.Documents;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.Views;
using Xunit;
using static RibbonXmlEditor.Tests.ViewModelTests;

namespace RibbonXmlEditor.Tests;

/// <summary>The built-in manual (Help > Documentation) stays complete as the schema and features grow.</summary>
public class DocumentationTests
{
    private static string TextOf(Section s) => new TextRange(s.ContentStart, s.ContentEnd).Text;

    [Fact]
    public void EveryTopic_HasATitleAndContent_AndTheListMatchesTheDocument()
    {
        Sta(() =>
        {
            var w = new DocumentationWindow();
            Assert.True(w.Sections.Count >= 8, "expected a reasonably complete manual");
            Assert.Equal(w.Sections.Select(s => (string)s.Tag), w.TopicTitles);
            Assert.Equal(w.TopicTitles, w.TopicList.Select(t => t.Title));
            foreach (var s in w.Sections)
            {
                Assert.False(string.IsNullOrWhiteSpace((string)s.Tag));
                Assert.True(TextOf(s).Length > 200, $"topic '{s.Tag}' is too thin");
                Assert.Contains((string)s.Tag, TextOf(s)); // the heading repeats the title
            }
            Assert.Equal(w.TopicTitles.Count, w.TopicTitles.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.True(w.GoTo("Dockable panes"));
            Assert.False(w.GoTo("No such topic"));
        });
    }

    [Fact]
    public void FormatReference_MentionsEveryElementAndAttribute_OfTheSchema()
    {
        Sta(() =>
        {
            var w = new DocumentationWindow();
            var reference = w.Sections.Single(s => (string)s.Tag == "File format reference");
            var text = TextOf(reference);
            foreach (var def in RibbonSchema.All)
            {
                Assert.Contains(def.XmlName, text);
                foreach (var a in def.Attributes)
                    Assert.Contains(a.XmlName, text);
            }
        });
    }

    [Fact]
    public void Manual_CoversTheHeadlineFeatures()
    {
        Sta(() =>
        {
            var w = new DocumentationWindow();
            var all = string.Join("\n", w.Sections.Select(TextOf));
            foreach (var phrase in new[]
            {
                "Add dockable toggle", "Drag and drop", "Disable", "Enabled in Revit", "Select Tab DLL",
                "Ribbon preview", "Backups", "Ctrl+S", "Alt+", "Restart Revit", "IDockablePaneProvider", "TryGetDockablePane",
            })
            {
                Assert.Contains(phrase, all);
            }
        });
    }
}
