namespace RibbonXmlEditor.Tests;

/// <summary>Locations of real production files on the developer machine. Tests skip when absent.</summary>
internal static class TestPaths
{
    public const string RepoRoot = @"C:\Visual Studio Files\BTT_ACCORevit-Ribbons";
    public const string DeployedRoot = @"C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons";

    public static readonly string[] ProductionRibbons =
    {
        @"00.Template_Tab\00.Template_Tab.ribbon",
        @"01.ConTech_Tab\01.ConTech_Tab.ribbon",
        @"02.Engineering_BIM_Team_Tab\02.Engineering_BIM_Team_Tab.ribbon",
        @"03.Engineering_Mechanical_Tab\03.Engineering_Mechanical_Tab.ribbon",
        @"RevitRibbon_MainSourceCode\Templates\ButtonStructureTemplate.ribbon",
        @"RevitRibbon_MainSourceCode\Templates\Template_Tab.ribbon",
    };

    public static string Mechanical => Path.Combine(RepoRoot, ProductionRibbons[3]);
}
