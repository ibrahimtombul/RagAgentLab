using RagAgentLab.Tools;

namespace RagAgentLab.Tests;

/// <summary>Tests for the business-day arithmetic exposed to the agent.</summary>
public sealed class WorkdayToolTests
{
    private readonly WorkdayTool _tool = new();

    [Theory]
    // 2026-06-15 is a Monday; ten working days earlier is Monday 2026-06-01.
    [InlineData("2026-06-15", -10, "2026-06-01")]
    [InlineData("2026-06-15", 1, "2026-06-16")]
    [InlineData("2026-06-19", 1, "2026-06-22")]  // Friday + 1 working day skips the weekend
    [InlineData("2026-06-15", 0, "2026-06-15")]
    [InlineData("2026-06-15", 5, "2026-06-22")]
    public void AddBusinessDays_SkipsWeekends(string start, int days, string expected) =>
        Assert.StartsWith(expected, _tool.AddBusinessDays(start, days));

    [Fact]
    public void AddBusinessDays_NeverLandsOnAWeekend()
    {
        foreach (var days in Enumerable.Range(1, 30))
        {
            var result = _tool.AddBusinessDays("2026-06-15", days);

            Assert.DoesNotContain("Saturday", result);
            Assert.DoesNotContain("Sunday", result);
        }
    }

    [Theory]
    [InlineData("15.06.2026")]
    [InlineData("2026-13-01")]
    [InlineData("yarın")]
    public void AddBusinessDays_ReturnsErrorTextForAnUnparseableDate(string start) =>
        Assert.StartsWith("ERROR:", _tool.AddBusinessDays(start, 1));

    [Fact]
    public void AddBusinessDays_RejectsAbsurdRanges() =>
        Assert.StartsWith("ERROR:", _tool.AddBusinessDays("2026-06-15", 100_000));

    [Fact]
    public void GetToday_ReturnsTodayInIsoFormat() =>
        Assert.StartsWith(DateTime.Today.ToString("yyyy-MM-dd"), _tool.GetToday());
}
