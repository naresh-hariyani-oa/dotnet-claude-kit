using CWM.RoslynNavigator.Tools;

namespace CWM.RoslynNavigator.Tests.Tools;

/// <summary>
/// Regression tests for issue #19: net10.0 projects were reported as "netcoreapp1.0"
/// because the longest NET-prefixed preprocessor symbol (NETCOREAPP1_0_OR_GREATER)
/// won over the exact TFM symbol (NET10_0).
/// </summary>
public class DetectTargetFrameworkTests
{
    [Fact]
    public void DetectFromPreprocessorSymbols_Net10_ReturnsNet10()
    {
        // Real symbol set the SDK defines for a net10.0 compilation
        string[] symbols =
        [
            "TRACE", "DEBUG", "NET", "NET10_0", "NETCOREAPP",
            "NET5_0_OR_GREATER", "NET6_0_OR_GREATER", "NET7_0_OR_GREATER",
            "NET8_0_OR_GREATER", "NET9_0_OR_GREATER", "NET10_0_OR_GREATER",
            "NETCOREAPP1_0_OR_GREATER", "NETCOREAPP1_1_OR_GREATER",
            "NETCOREAPP2_0_OR_GREATER", "NETCOREAPP2_1_OR_GREATER",
            "NETCOREAPP2_2_OR_GREATER", "NETCOREAPP3_0_OR_GREATER",
            "NETCOREAPP3_1_OR_GREATER",
        ];

        Assert.Equal("net10.0", GetProjectGraphTool.DetectFromPreprocessorSymbols(symbols));
    }

    [Fact]
    public void DetectFromPreprocessorSymbols_Net8_ReturnsNet8()
    {
        string[] symbols =
        [
            "TRACE", "NET", "NET8_0", "NETCOREAPP",
            "NET5_0_OR_GREATER", "NET6_0_OR_GREATER", "NET7_0_OR_GREATER", "NET8_0_OR_GREATER",
            "NETCOREAPP1_0_OR_GREATER", "NETCOREAPP2_0_OR_GREATER",
            "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER",
        ];

        Assert.Equal("net8.0", GetProjectGraphTool.DetectFromPreprocessorSymbols(symbols));
    }

    [Fact]
    public void DetectFromPreprocessorSymbols_NetStandard20_ReturnsNetStandard20()
    {
        string[] symbols =
        [
            "TRACE", "NETSTANDARD", "NETSTANDARD2_0",
            "NETSTANDARD1_0_OR_GREATER", "NETSTANDARD1_1_OR_GREATER",
            "NETSTANDARD1_6_OR_GREATER", "NETSTANDARD2_0_OR_GREATER",
        ];

        Assert.Equal("netstandard2.0", GetProjectGraphTool.DetectFromPreprocessorSymbols(symbols));
    }

    [Fact]
    public void DetectFromPreprocessorSymbols_NetCoreApp31_ReturnsNetCoreApp31()
    {
        string[] symbols =
        [
            "TRACE", "NETCOREAPP", "NETCOREAPP3_1",
            "NETCOREAPP1_0_OR_GREATER", "NETCOREAPP2_0_OR_GREATER",
            "NETCOREAPP3_0_OR_GREATER", "NETCOREAPP3_1_OR_GREATER",
        ];

        Assert.Equal("netcoreapp3.1", GetProjectGraphTool.DetectFromPreprocessorSymbols(symbols));
    }

    [Fact]
    public void DetectFromPreprocessorSymbols_NetFramework48_ReturnsNet48()
    {
        string[] symbols =
        [
            "TRACE", "NETFRAMEWORK", "NET48",
            "NET20_OR_GREATER", "NET35_OR_GREATER", "NET40_OR_GREATER",
            "NET45_OR_GREATER", "NET472_OR_GREATER", "NET48_OR_GREATER",
        ];

        Assert.Equal("net48", GetProjectGraphTool.DetectFromPreprocessorSymbols(symbols));
    }

    [Fact]
    public void DetectFromPreprocessorSymbols_NoTfmSymbols_ReturnsNull()
    {
        string[] symbols = ["TRACE", "DEBUG", "MY_CUSTOM_FLAG"];

        Assert.Null(GetProjectGraphTool.DetectFromPreprocessorSymbols(symbols));
    }

    // Multi-targeting: Roslyn loads one project flavor per TFM. The first entry of
    // <TargetFrameworks> must NOT be reported for every flavor — the net8.0 flavor of a
    // "net10.0;net8.0" project is net8.0.

    [Fact]
    public void SelectMultiTargetTfm_FlavorNameWins_ForNet8FlavorOfNet10Net8Project()
    {
        var tfm = GetProjectGraphTool.SelectMultiTargetTfm(
            "MyProject(net8.0)",
            ["net10.0", "net8.0"],
            preprocessorSymbols: null);

        Assert.Equal("net8.0", tfm);
    }

    [Fact]
    public void SelectMultiTargetTfm_PreprocessorSymbolsWin_WhenNameHasNoFlavorSuffix()
    {
        string[] net8Symbols = ["TRACE", "NET", "NET8_0", "NETCOREAPP", "NET8_0_OR_GREATER"];

        var tfm = GetProjectGraphTool.SelectMultiTargetTfm(
            "MyProject",
            ["net10.0", "net8.0"],
            net8Symbols);

        Assert.Equal("net8.0", tfm);
    }

    [Fact]
    public void SelectMultiTargetTfm_MatchesOsSpecificEntry_ByBaseTfmPrefix()
    {
        string[] net10Symbols = ["TRACE", "NET", "NET10_0", "NETCOREAPP", "WINDOWS"];

        var tfm = GetProjectGraphTool.SelectMultiTargetTfm(
            "MyProject",
            ["net10.0-windows", "net8.0"],
            net10Symbols);

        Assert.Equal("net10.0-windows", tfm);
    }

    [Fact]
    public void SelectMultiTargetTfm_FallsBackToFirstEntry_WithoutNameOrSymbols()
    {
        var tfm = GetProjectGraphTool.SelectMultiTargetTfm(
            "MyProject",
            ["net10.0", "net8.0"],
            preprocessorSymbols: null);

        Assert.Equal("net10.0", tfm);
    }

    [Theory]
    [InlineData("MyProject(net8.0)", "net8.0")]
    [InlineData("My.Project(net10.0-windows)", "net10.0-windows")]
    [InlineData("MyProject", null)]
    [InlineData("Weird(Name)Project", null)] // parens not at the end
    [InlineData("MyProject()", null)]
    public void ExtractTfmFromProjectName_ParsesFlavorSuffix(string projectName, string? expected)
    {
        Assert.Equal(expected, GetProjectGraphTool.ExtractTfmFromProjectName(projectName));
    }
}
