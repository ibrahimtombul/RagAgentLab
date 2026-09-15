namespace RagAgentLab.Embeddings;

/// <summary>
/// Storage and nearest-neighbour search over embedding vectors.
/// <para>
/// The methods are asynchronous even though the current implementation is a plain
/// in-memory list: a real store (Qdrant, pgvector, Azure AI Search) is I/O bound, so
/// keeping the signatures async means swapping the implementation later does not ripple
/// through the RAG pipeline or the agent layer.
/// </para>
/// </summary>
public interface IVectorStore
{
    /// <summary>Inserts or replaces records, keyed by <see cref="DocumentChunk.Id"/>.</summary>
    /// <param name="records">Records to store.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default);

    /// <summary>Returns the <paramref name="topK"/> records most similar to the query vector.</summary>
    /// <param name="queryVector">Embedding of the user's question.</param>
    /// <param name="topK">How many results to return.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>Hits ordered by descending similarity.</returns>
    Task<IReadOnlyList<SearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken = default);

    /// <summary>Number of records currently stored.</summary>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}
