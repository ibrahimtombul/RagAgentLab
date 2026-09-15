using RagAgentLab.Tools;

namespace RagAgentLab.Tests;

/// <summary>
/// Tests for the lookup table behind the agent's wage tools. The point of moving these figures
/// out of the corpus was to make them exact, so the exactness is worth asserting.
/// </summary>
public sealed class MinimumWageToolTests
{
    private readonly MinimumWageTool _tool = new();

    [Fact]
    public void GetMinimumWage_ReturnsBothPeriodsForAYearThatChangedMidway()
    {
        var result = _tool.GetMinimumWage(2015);

        Assert.Contains("949,07", result);      // first half, net
        Assert.Contains("1.201,50", result);    // first half, gross
        Assert.Contains("1.000,54", result);    // second half, net
    }

    [Fact]
    public void GetMinimumWage_FiltersToTheRequestedHalf()
    {
        var result = _tool.GetMinimumWage(2015, "ikinci");

        Assert.Contains("1.000,54", result);
        Assert.DoesNotContain("949,07", result);
    }

    [Fact]
    public void GetMinimumWage_LeadsWithTheNetFigure()
    {
        // Net is what people mean when they compare wages, so it is stated first.
        var result = _tool.GetMinimumWage(2024);

        Assert.True(
            result.IndexOf("net", StringComparison.Ordinal) <
            result.IndexOf("brüt", StringComparison.Ordinal));
    }

    [Fact]
    public void GetMinimumWage_DefaultsToTheCurrentYear()
    {
        // The model has no idea what today's date is, so the tool resolves it.
        var result = _tool.GetMinimumWage();

        Assert.Contains(DateTime.Today.Year.ToString(), result);
    }

    [Fact]
    public void GetMinimumWage_ReportsTheCoveredRangeForAnUnknownYear()
    {
        var result = _tool.GetMinimumWage(1999);

        Assert.StartsWith("ERROR:", result);
        Assert.Contains("2005", result);
    }

    [Fact]
    public void CompareSalary_UsesTheNetWageByDefault()
    {
        // 64000 / 28075.00 = 2.2796... -> 2,28
        var result = _tool.CompareSalaryToMinimumWage(64000, year: 2026);

        Assert.Contains("net", result);
        Assert.Contains("28.075,00", result);
        Assert.Contains("2,28", result);
    }

    [Fact]
    public void CompareSalary_CanUseTheGrossWageInstead()
    {
        // 64000 / 33030.00 = 1.9376... -> 1,94
        var result = _tool.CompareSalaryToMinimumWage(64000, "brut", 2026);

        Assert.Contains("brüt", result);
        Assert.Contains("33.030,00", result);
        Assert.Contains("1,94", result);
    }

    [Fact]
    public void CompareSalary_UsesTheLastPeriodOfAYearThatChangedMidway()
    {
        // 2023's second half is the one still in force at the end of that year.
        var result = _tool.CompareSalaryToMinimumWage(22804.64, year: 2023);

        Assert.Contains("11.402,32", result);
        Assert.Contains("2,00", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5000)]
    public void CompareSalary_RejectsANonPositiveSalary(double salary) =>
        Assert.StartsWith("ERROR:", _tool.CompareSalaryToMinimumWage(salary));
}
