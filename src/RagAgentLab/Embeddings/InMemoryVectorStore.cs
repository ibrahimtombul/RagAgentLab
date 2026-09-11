using System.Collections.Concurrent;

namespace RagAgentLab.Embeddings;

/// <summary>
/// Vector store that keeps everything in process memory and scores every record on each
/// query (a brute-force / exact k-NN scan).
/// <para>
/// This is deliberate for a demo of this size: a few hundred chunks are scanned in under a
/// millisecond, there is no container to run, and the retrieval quality is exact rather
/// than approximate. Once the corpus grows past a few tens of thousands of chunks the scan
/// becomes the bottleneck and an ANN index (Qdrant, pgvector's HNSW) is the right answer —
/// at which point only this class is replaced, because callers depend on
/// <see cref="IVectorStore"/>.
/// </para>
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, VectorRecord> _records = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _records[record.Chunk.Id] = record;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be greater than zero.");
        }

        var hits = _records.Values
            .Select(record => new SearchResult(record.Chunk, VectorMath.CosineSimilarity(queryVector, record.Vector)))
            .OrderByDescending(hit => hit.Score)
            .Take(topK)
            .ToArray();

        return Task.FromResult<IReadOnlyList<SearchResult>>(hits);
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) => Task.FromResult(_records.Count);
}
