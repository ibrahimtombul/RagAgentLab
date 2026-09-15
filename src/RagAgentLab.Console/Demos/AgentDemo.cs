using RagAgentLab.Agents;
using RagAgentLab.Infrastructure;
using RagAgentLab.Rag;

namespace RagAgentLab.Demos;

/// <summary>
/// Stage 3 demo: the agent answers questions by choosing its own tools, and every decision it
/// makes is printed as it happens.
/// <para>
/// The scripted questions are picked to exercise different routes through the tool set: one
/// that needs only retrieval, one that needs only arithmetic, one that needs retrieval and
/// then arithmetic, and one that needs retrieval and then date maths.
/// </para>
/// </summary>
public sealed class AgentDemo
{
    private static readonly (string Question, string Expectation)[] SampleQuestions =
    [
        ("Yurt dışından yılda en fazla kaç iş günü çalışabilirim?",
         "policy lookup only"),
        ("Aylık 750 TL internet katkısı bir yılda toplam kaç TL eder?",
         "calculator only"),
        ("Yurt içinde 4 günlük bir iş seyahatinde toplam yemek harcırahım ne kadar olur?",
         "policy lookup, then calculator"),
        ("Bugün ayın kaçı ve haftanın hangi günü?",
         "today's date"),
    ];

    private readonly KnowledgeBaseIngestor _ingestor;
    private readonly IAgent _agent;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public AgentDemo(KnowledgeBaseIngestor ingestor, IAgent agent)
    {
        _ingestor = ingestor;
        _agent = agent;
    }

    /// <summary>Ingests the knowledge base, runs the scripted questions, then goes interactive.</summary>
    /// <param name="singleQuestion">
    /// When given, only this question is asked and the scripted set is skipped. Handy for
    /// demoing or debugging one route through the tool set.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    public async Task RunAsync(string? singleQuestion = null, CancellationToken cancellationToken = default)
    {
        ConsoleUi.Section("Stage 3 - Knowledge base ingestion");
        var report = await _ingestor.IngestAsync(cancellationToken);
        ConsoleUi.Success($"{report.DocumentCount} document(s) -> {report.ChunkCount} chunk(s) indexed.");

        ConsoleUi.Section("Stage 3 - Agent with tools");
        ConsoleUi.Info("Tools available to the model: search_hr_policy, calculate, get_today, add_business_days");

        if (!string.IsNullOrWhiteSpace(singleQuestion))
        {
            await AskAndPrintAsync(singleQuestion, expectation: null, cancellationToken);
            return;
        }

        foreach (var (question, expectation) in SampleQuestions)
        {
            await AskAndPrintAsync(question, expectation, cancellationToken);
        }

        await RunInteractiveLoopAsync(cancellationToken);
    }

    /// <summary>Runs one agent turn and prints the trace summary underneath the live log.</summary>
    private async Task AskAndPrintAsync(
        string question,
        string? expectation,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        ConsoleUi.Step("Question", question);
        if (expectation is not null)
        {
            ConsoleUi.Info($"  (expected route: {expectation})");
        }

        var result = await _agent.RunAsync(
            question,
            new AgentRunOptions
            {
                OnToolCallStarted = (tool, arguments) =>
                    ConsoleUi.Step($"tool call", $"{tool}({arguments})"),
                OnToolCallCompleted = step =>
                    ConsoleUi.Step("        result", $"{step.Result} [{step.Duration.TotalMilliseconds:F0} ms]"),
            },
            cancellationToken);

        if (result.Steps.Count == 0)
        {
            ConsoleUi.Warn(AgentOutputHeuristics.LooksLikeTextualToolCall(result.Answer)
                ? "The model wrote a tool call as plain text instead of calling the tool."
                : "The model answered without calling any tool.");
        }

        ConsoleUi.Answer(result.Answer);
        ConsoleUi.Info($"({result.Steps.Count} tool call(s), {result.Duration.TotalSeconds:F1}s)");
    }

    /// <summary>Lets the reviewer try their own questions; skipped when input is redirected.</summary>
    private async Task RunInteractiveLoopAsync(CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        ConsoleUi.Section("Ask the agent (empty line to quit)");

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("  > ");
            var question = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(question))
            {
                break;
            }

            await AskAndPrintAsync(question, expectation: null, cancellationToken);
        }
    }
}
