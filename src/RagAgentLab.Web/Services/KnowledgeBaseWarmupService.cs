using RagAgentLab.Embeddings;
using RagAgentLab.Rag;
using RagAgentLab.Shop;

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
    private readonly ProductCatalogIndexer _catalogIndexer;
    private readonly IVectorStore _vectorStore;
    private readonly KnowledgeBaseState _state;
    private readonly ILogger<KnowledgeBaseWarmupService> _logger;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public KnowledgeBaseWarmupService(
        KnowledgeBaseIngestor ingestor,
        ProductCatalogIndexer catalogIndexer,
        IVectorStore vectorStore,
        KnowledgeBaseState state,
        ILogger<KnowledgeBaseWarmupService> logger)
    {
        _ingestor = ingestor;
        _catalogIndexer = catalogIndexer;
        _vectorStore = vectorStore;
        _state = state;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var report = await _ingestor.IngestAsync(stoppingToken);

            // Product descriptions are prose too, and live in the operational database rather
            // than in the document folder.
            var products = await _catalogIndexer.IndexAsync(stoppingToken);

            _logger.LogInformation(
                "Knowledge base ready: {Documents} document(s), {Chunks} chunk(s), " +
                "{Products} product description(s) indexed, in {Seconds:F1}s.",
                report.DocumentCount, report.ChunkCount, products, report.Duration.TotalSeconds);

            // Counted from the store rather than from the report: with a persistent store the
            // corpus also holds notes added from the chat in earlier runs, which no document
            // ingestion produced.
            _state.MarkReady(report.DocumentCount, await _vectorStore.CountAsync(stoppingToken));
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
