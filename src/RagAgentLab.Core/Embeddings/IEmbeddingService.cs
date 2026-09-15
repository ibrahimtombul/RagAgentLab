namespace RagAgentLab.Embeddings;

/// <summary>
/// Turns text into embedding vectors. The rest of the application depends on this
/// interface rather than on Semantic Kernel's embedding types, so the vector store and
/// the RAG pipeline stay free of any SDK-specific types.
/// <para>
/// Documents and queries have separate methods on purpose. Several embedding models —
/// nomic-embed-text among them — are trained asymmetrically and expect a stored passage to
/// be marked differently from a search query. Making that distinction part of the interface
/// means a caller cannot accidentally embed a question the way a document is embedded, which
/// is a silent retrieval-quality bug rather than a crash.
/// </para>
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Embeds a search query, such as the user's question.</summary>
    /// <param name="query">The query text.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>The embedding vector.</returns>
    Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>Embeds document chunks for storage, batching the requests to the model server.</summary>
    /// <param name="documents">Chunk texts, in order.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>Embedding vectors in the same order as <paramref name="documents"/>.</returns>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(
        IReadOnlyList<string> documents,
        CancellationToken cancellationToken = default);
}
