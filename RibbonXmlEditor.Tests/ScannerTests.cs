using RibbonXmlEditor.Services;
using Xunit;

namespace RibbonXmlEditor.Tests;

public class ScannerTests
{
    private const string Parallel = "RevitRibbon_MainSourceCode.Unique_Button_Classes.Mechanical.Cmd_Parallel";

    public static IEnumerable<object[]> MechanicalDlls() => new[]
    {
        new object[] { Path.Combine(TestPaths.DeployedRoot, @"Engineering\Mechanical\2025\03.Engineering_Mechanical_Tab.dll") }, // net8
        new object[] { Path.Combine(TestPaths.DeployedRoot, @"Engineering\Mechanical\2024\03.Engineering_Mechanical_Tab.dll") }, // net48
        new object[] { Path.Combine(TestPaths.RepoRoot, @"03.Engineering_Mechanical_Tab\bin\Release\2025\03.Engineering_Mechanical_Tab.dll") },
    };

    [Theory]
    [MemberData(nameof(MechanicalDlls))]
    public void Scan_FindsCommandClasses_WithoutLoadingRevitApi(string dll)
    {
        if (!File.Exists(dll))
            return;

        var result = CommandClassScanner.Scan(dll);
        Assert.Null(result.Error);
        Assert.Contains(Parallel, result.ClassNames);
        Assert.Contains("RevitRibbon_MainSourceCode.Cmd_About", result.ClassNames);
        Assert.True(result.ClassNames.Count > 5, $"only {result.ClassNames.Count} classes found");
        Assert.Equal(result.ClassNames.OrderBy(n => n, StringComparer.Ordinal), result.ClassNames);
    }

    [Fact]
    public void Scan_NonAssembly_ReportsErrorInsteadOfThrowing()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "this is not a dll");
            var result = CommandClassScanner.Scan(path);
            Assert.NotNull(result.Error);
            Assert.Empty(result.ClassNames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Scan_MissingFile_ReportsError()
    {
        var result = CommandClassScanner.Scan(@"C:\definitely\missing\x.dll");
        Assert.NotNull(result.Error);
    }
}
