using Microsoft.Extensions.Logging;
using RagAgentLab.Embeddings;

namespace RagAgentLab.Shop;

/// <summary>
/// Indexes product descriptions into the vector store.
/// <para>
/// The products table is mostly facts — code, price, stock — and those are answered with SQL. Its
/// description column is not: it is prose written for a person, and the questions people ask of it
/// are about meaning rather than values. "Something that keeps a drink warm until lunch" names
/// neither the product nor any word in its row, and no <c>WHERE</c> clause will find it.
/// </para>
/// <para>
/// So the same row is served two ways, and the split is per question rather than per table. The
/// chunks are marked <see cref="ChunkOrigin.Product"/> so a catalogue search cannot come back with
/// a paragraph of the leave policy, and so that re-ingesting the document folder leaves them alone.
/// </para>
/// </summary>
public sealed class ProductCatalogIndexer
{
    private readonly ShopDatabase _database;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILogger<ProductCatalogIndexer> _logger;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public ProductCatalogIndexer(
        ShopDatabase database,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILogger<ProductCatalogIndexer> logger)
    {
        _database = database;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _logger = logger;
    }

    /// <summary>Embeds every product description that is new or has changed.</summary>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>How many descriptions were embedded on this run.</returns>
    public async Task<int> IndexAsync(CancellationToken cancellationToken = default)
    {
        var chunks = await ReadCatalogAsync(cancellationToken);
        if (chunks.Count == 0)
        {
            return 0;
        }

        var stored = _vectorStore is IFingerprintedVectorStore fingerprinted
            ? await fingerprinted.GetFingerprintsAsync(cancellationToken)
            : new Dictionary<string, string>();

        var fingerprints = chunks.ToDictionary(
            chunk => chunk.Id,
            chunk => ChunkFingerprint.Compute(_embeddingService.ModelId, chunk.ToContextualText()),
            StringComparer.Ordinal);

        var changed = chunks
            .Where(chunk => !stored.TryGetValue(chunk.Id, out var stale) || stale != fingerprints[chunk.Id])
            .ToArray();

        if (changed.Length == 0)
        {
            return 0;
        }

        var texts = changed.Select(chunk => chunk.ToContextualText()).ToArray();
        var vectors = await _embeddingService.EmbedDocumentsAsync(texts, cancellationToken);

        await _vectorStore.UpsertAsync(
            changed.Select((chunk, index) =>
                new VectorRecord(chunk, vectors[index], fingerprints[chunk.Id])).ToArray(),
            cancellationToken);

        _logger.LogInformation("Indexed {Count} product description(s).", changed.Length);
        return changed.Length;
    }

    /// <summary>Reads each product as a single chunk. Descriptions are short enough not to split.</summary>
    private async Task<IReadOnlyList<DocumentChunk>> ReadCatalogAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT code, name, category, description FROM products ORDER BY code;";

        var chunks = new List<DocumentChunk>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var code = reader.GetString(0);
            var name = reader.GetString(1);
            var category = reader.GetString(2);
            var description = reader.GetString(3);

            chunks.Add(new DocumentChunk(
                Id: $"product:{code}",
                SourceName: code,
                DocumentTitle: $"{name} — {category}",
                ChunkIndex: 0,
                Text: description)
            {
                Origin = ChunkOrigin.Product,
            });
        }

        return chunks;
    }
}
