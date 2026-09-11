using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;
using RagAgentLab.Embeddings;

namespace RagAgentLab.Rag;

/// <summary>
/// Builds the knowledge base: reads the source documents, chunks them, embeds every chunk
/// and writes the vectors into the store. This is the "indexing" half of RAG and normally
/// runs offline; the demo runs it at start-up because the store is in memory.
/// </summary>
public sealed class KnowledgeBaseIngestor
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly RagOptions _options;
    private readonly ILogger<KnowledgeBaseIngestor> _logger;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public KnowledgeBaseIngestor(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IOptions<RagOptions> options,
        ILogger<KnowledgeBaseIngestor> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Ingests every <c>*.txt</c> file in the configured data directory.</summary>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>Counts and timings for the run.</returns>
    /// <exception cref="DirectoryNotFoundException">The configured data directory does not exist.</exception>
    /// <exception cref="InvalidOperationException">The data directory contains no documents.</exception>
    public async Task<IngestionReport> IngestAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var directory = Path.IsPathRooted(_options.DataDirectory)
            ? _options.DataDirectory
            : Path.Combine(AppContext.BaseDirectory, _options.DataDirectory);

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Knowledge base directory not found: {directory}");
        }

        var files = Directory.GetFiles(directory, "*.txt", SearchOption.AllDirectories).Order().ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException($"No .txt documents found in {directory}.");
        }

        // 1) Read and chunk every document.
        var chunks = new List<DocumentChunk>();
        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var fileName = Path.GetFileName(file);

            var pieces = TextChunker.Split(text, _options.ChunkSize, _options.ChunkOverlap);
            chunks.AddRange(pieces.Select((piece, index) =>
                new DocumentChunk($"{fileName}#{index}", fileName, index, piece)));

            _logger.LogDebug("{File}: {Chunks} chunk(s).", fileName, pieces.Count);
        }

        // 2) Embed all chunks, then 3) store them.
        var vectors = await _embeddingService.EmbedBatchAsync(
            chunks.Select(c => c.Text).ToArray(), cancellationToken);

        var records = chunks
            .Zip(vectors, (chunk, vector) => new VectorRecord(chunk, vector))
            .ToArray();

        await _vectorStore.UpsertAsync(records, cancellationToken);

        stopwatch.Stop();

        return new IngestionReport(
            DocumentCount: files.Length,
            ChunkCount: records.Length,
            EmbeddingDimensions: records.Length > 0 ? records[0].Vector.Length : 0,
            Duration: stopwatch.Elapsed);
    }
}
