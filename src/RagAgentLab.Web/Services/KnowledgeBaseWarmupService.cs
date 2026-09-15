using RagAgentLab.Rag;

namespace RagAgentLab.Web.Services;

/// <summary>
/// Indexes the corpus once, in the background, when the web application starts.
/// <para>
/// It runs as a <see cref="BackgroundService"/> rather than inline in <c>Program.cs</c> so a
/// slow or unavailable model server delays only the first answer, not the web server itself.
/// The in-memory store is rebuilt on every start; that is a second or two for this corpus and
/// is the piece that would move to a persistent store and a separate job once documents can
/// be uploaded.
/// </para>
/// </summary>
public sealed class KnowledgeBaseWarmupService : BackgroundService
{
    private readonly KnowledgeBaseIngestor _ingestor;
    private readonly KnowledgeBaseState _state;
    private readonly ILogger<KnowledgeBaseWarmupService> _logger;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public KnowledgeBaseWarmupService(
        KnowledgeBaseIngestor ingestor,
        KnowledgeBaseState state,
        ILogger<KnowledgeBaseWarmupService> logger)
    {
        _ingestor = ingestor;
        _state = state;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var report = await _ingestor.IngestAsync(stoppingToken);

            _logger.LogInformation(
                "Knowledge base ready: {Documents} document(s), {Chunks} chunk(s) in {Seconds:F1}s.",
                report.DocumentCount, report.ChunkCount, report.Duration.TotalSeconds);

            _state.MarkReady(report.DocumentCount, report.ChunkCount);
        }
        catch (OperationCanceledException)
        {
            // The application is shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Knowledge base ingestion failed.");
            _state.MarkFailed(ex.Message);
        }
    }
}
