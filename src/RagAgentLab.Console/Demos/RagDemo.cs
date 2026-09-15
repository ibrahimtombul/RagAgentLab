using RagAgentLab.Infrastructure;
using RagAgentLab.Rag;

namespace RagAgentLab.Demos;

/// <summary>
/// Stage 2 demo: builds the knowledge base, then answers questions about it.
/// <para>
/// The first question is deliberately answered twice — once without any retrieved context
/// and once with it — because that side-by-side is the clearest way to show what RAG buys
/// you: the model has never heard of the fictional company, so the ungrounded answer is
/// either a refusal or an invention, while the grounded one quotes the real policy.
/// </para>
/// </summary>
public sealed class RagDemo
{
    private static readonly string[] SampleQuestions =
    [
        "6 yıldır çalışan birinin yıllık izin hakkı kaç gün?",
        "Ofise haftada kaç gün gelmek zorundayım ve hangi günler?",
        "Yurt içi bir seyahatte günlük yemek harcırahı ne kadar?",
        "Eğitim bütçem ne kadar ve bir sonraki yıla devreder mi?",
    ];

    private readonly KnowledgeBaseIngestor _ingestor;
    private readonly IRagPipeline _ragPipeline;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public RagDemo(KnowledgeBaseIngestor ingestor, IRagPipeline ragPipeline)
    {
        _ingestor = ingestor;
        _ragPipeline = ragPipeline;
    }

    /// <summary>Runs ingestion, the scripted questions and an interactive prompt.</summary>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ConsoleUi.Section("Stage 2 - Knowledge base ingestion");
        var report = await _ingestor.IngestAsync(cancellationToken);
        ConsoleUi.Success(
            $"{report.DocumentCount} document(s) -> {report.ChunkCount} chunk(s) " +
            $"({report.EmbeddedChunkCount} embedded, {report.ReusedChunkCount} reused) " +
            $"in {report.Duration.TotalSeconds:F1}s");

        await ShowGroundingComparisonAsync(SampleQuestions[0], cancellationToken);

        ConsoleUi.Section("Stage 2 - Sample questions");
        foreach (var question in SampleQuestions.Skip(1))
        {
            await AskAndPrintAsync(question, cancellationToken);
        }

        await RunInteractiveLoopAsync(cancellationToken);
    }

    /// <summary>Answers the same question with and without retrieved context.</summary>
    private async Task ShowGroundingComparisonAsync(string question, CancellationToken cancellationToken)
    {
        ConsoleUi.Section("Stage 2 - With and without retrieval");
        ConsoleUi.Step("Question", question);

        ConsoleUi.Info("");
        ConsoleUi.Warn("No retrieval (the model has never seen this company's policies):");
        ConsoleUi.Answer(await _ragPipeline.AskWithoutContextAsync(question, cancellationToken));

        ConsoleUi.Success("With retrieval:");
        await AskAndPrintAsync(question, cancellationToken, printQuestion: false);
    }

    /// <summary>Runs one RAG round-trip and prints the retrieved sources plus the answer.</summary>
    private async Task AskAndPrintAsync(
        string question,
        CancellationToken cancellationToken,
        bool printQuestion = true)
    {
        if (printQuestion)
        {
            Console.WriteLine();
            ConsoleUi.Step("Question", question);
        }

        var result = await _ragPipeline.AskAsync(question, cancellationToken);

        foreach (var (source, rank) in result.Sources.Select((s, i) => (s, i + 1)))
        {
            ConsoleUi.Step(
                $"Match #{rank}",
                $"{source.Chunk.SourceName} (chunk {source.Chunk.ChunkIndex}) - similarity {source.Score:F3}");
        }

        ConsoleUi.Answer(result.Answer);
        ConsoleUi.Info($"({result.Duration.TotalSeconds:F1}s)");
    }

    /// <summary>Lets the reviewer type their own questions; skipped when input is redirected.</summary>
    private async Task RunInteractiveLoopAsync(CancellationToken cancellationToken)
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        ConsoleUi.Section("Ask your own question (empty line to quit)");

        while (!cancellationToken.IsCancellationRequested)
        {
            Console.Write("  > ");
            var question = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(question))
            {
                break;
            }

            await AskAndPrintAsync(question, cancellationToken, printQuestion: false);
        }
    }
}
