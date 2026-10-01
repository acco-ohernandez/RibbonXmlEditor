using System.Text.RegularExpressions;
using RibbonXmlEditor.Schema;
using Xunit;

namespace RibbonXmlEditor.Tests;

/// <summary>
/// Keeps the editor's schema table and the Revit-side parser (RibbonBuilder.cs) in sync.
/// The parser source is embedded in this test assembly and its XmlNames constants are read with a regex.
/// </summary>
public class SchemaSyncTests
{
    private static string BuilderSource()
    {
        using var stream = typeof(SchemaSyncTests).Assembly.GetManifestResourceStream("RibbonBuilder.cs")
            ?? throw new InvalidOperationException("RibbonBuilder.cs is not embedded in the test assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static (HashSet<string> elements, HashSet<string> attributes) BuilderNames(string source)
    {
        int start = source.IndexOf("public static class XmlNames", StringComparison.Ordinal);
        Assert.True(start >= 0, "XmlNames class not found in RibbonBuilder.cs");
        int elementsAt = source.IndexOf("// Elements", start, StringComparison.Ordinal);
        int attributesAt = source.IndexOf("// Attributes", start, StringComparison.Ordinal);
        int end = source.IndexOf("private sealed class Session", start, StringComparison.Ordinal);
        Assert.True(elementsAt > 0 && attributesAt > elementsAt && end > attributesAt, "XmlNames section markers not found");

        static HashSet<string> Consts(string block)
            => Regex.Matches(block, @"public const string \w+ = ""([a-z]+)"";")
                    .Select(m => m.Groups[1].Value)
                    .ToHashSet(StringComparer.Ordinal);

        return (Consts(source[elementsAt..attributesAt]), Consts(source[attributesAt..end]));
    }

    [Fact]
    public void BuilderElementNames_MatchSchema()
    {
        var (elements, _) = BuilderNames(BuilderSource());
        var schema = RibbonSchema.All.Select(d => d.XmlName).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(schema.OrderBy(s => s), elements.OrderBy(s => s));
    }

    [Fact]
    public void BuilderAttributeNames_MatchSchema()
    {
        var (_, attributes) = BuilderNames(BuilderSource());
        var schema = RibbonSchema.All.SelectMany(d => d.Attributes).Select(a => a.XmlName).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(schema.OrderBy(s => s), attributes.OrderBy(s => s));
    }

    [Fact]
    public void BuilderVersionConstant_MatchesHeaderComment()
    {
        var source = BuilderSource();
        var header = Regex.Match(source, @"^// Version (\d+\.\d+\.\d+) ", RegexOptions.Multiline);
        var constant = Regex.Match(source, @"public const string Version = ""(\d+\.\d+\.\d+)"";");
        Assert.True(header.Success && constant.Success, "version header or constant missing");
        Assert.Equal(header.Groups[1].Value, constant.Groups[1].Value);
    }

    [Fact]
    public void BuilderSkipsComments_ByIteratingElementsOnly()
    {
        // The disable feature relies on the parser ignoring XML comments everywhere.
        Assert.Contains("ChildNodes.OfType<XmlElement>()", BuilderSource());
    }

    [Fact]
    public void ProductionCopy_IsIdenticalToCanonicalCopy()
    {
        const string production = @"C:\Visual Studio Files\BTT_ACCORevit-Ribbons\RevitRibbon_MainSourceCode\Ribbon Builder\RibbonBuilder.cs";
        const string canonical = @"C:\Visual Studio Files\RibbonXmlEditor\RibbonXmlEditor\RibbonBuilder\RibbonBuilder.cs";
        if (!File.Exists(production) || !File.Exists(canonical))
            return; // other machine

        static string Normalize(string path) => File.ReadAllText(path).Replace("\r\n", "\n").TrimEnd();
        Assert.True(Normalize(production) == Normalize(canonical),
            "RibbonBuilder.cs in BTT_ACCORevit-Ribbons differs from the canonical copy in RibbonXmlEditor. Copy the canonical file over it.");
    }
}
