using RagAgentLab.Agents;

namespace RagAgentLab.Tests;

/// <summary>
/// Tests for the detector that recognises a tool call the model wrote as prose. Every
/// positive case below is a real reply observed from a local model during development.
/// </summary>
public sealed class AgentOutputHeuristicsTests
{
    private static readonly string[] ToolNames =
        ["search_hr_policy", "calculate", "get_today", "add_business_days"];

    [Theory]
    [InlineData("search_hr_policy({\"question\": \"Güncel asgari ücret nedir?\"})")]
    [InlineData("{\"name\":\"hr-search_hr_policy\",\"parameters\":{\"question\":\"Yurt dışı\"}}")]
    [InlineData("{\"function\": {\"name\": \"calculate\"}, \"arguments\": \"{}\"}")]
    [InlineData("  calculate({\"expression\": \"750 * 12\"})  ")]
    public void LooksLikeTextualToolCall_DetectsCallsWrittenAsText(string answer) =>
        Assert.True(AgentOutputHeuristics.LooksLikeTextualToolCall(answer, ToolNames));

    [Theory]
    [InlineData("Yurt dışından yılda en fazla 20 iş günü çalışabilirsiniz.")]
    [InlineData("Aylık 750 TL internet katkısı bir yılda toplam 9000 TL eder.")]
    [InlineData("Bu bilgi politika dokümanlarında yer almıyor.")]
    [InlineData("")]
    [InlineData("   ")]
    public void LooksLikeTextualToolCall_AcceptsOrdinaryAnswers(string answer) =>
        Assert.False(AgentOutputHeuristics.LooksLikeTextualToolCall(answer, ToolNames));

    [Fact]
    public void LooksLikeTextualToolCall_DoesNotFlagAnAnswerThatMerelyMentionsATool()
    {
        // A long, prose answer that happens to open with a tool's name is still an answer.
        const string answer =
            "calculate aracını kullanarak hesapladım: aylık 750 TL katkı, on iki ay boyunca " +
            "ödendiğinde yıllık toplam 9.000 TL ediyor. Bu tutar bordroya yansıtılır ve " +
            "uzaktan çalışan tüm personel için geçerlidir. Ayrıntılar politika dokümanında yer alır.";

        Assert.False(AgentOutputHeuristics.LooksLikeTextualToolCall(answer, ToolNames));
    }

    [Fact]
    public void LooksLikeTextualToolCall_WorksWithoutToolNames() =>
        Assert.True(AgentOutputHeuristics.LooksLikeTextualToolCall(
            "search_hr_policy({\"question\": \"x\"})"));
}
