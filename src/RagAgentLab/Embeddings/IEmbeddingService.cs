namespace RagAgentLab.Embeddings;

/// <summary>
/// Turns text into embedding vectors. The rest of the application depends on this
/// interface rather than on Semantic Kernel's embedding types, so the vector store and
/// the RAG pipeline stay free of any SDK-specific types.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Embeds a single piece of text (typically the user's question).</summary>
    /// <param name="text">Text to embed.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>The embedding vector.</returns>
    Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Embeds many pieces of text, batching the requests to the model server.</summary>
    /// <param name="texts">Texts to embed, in order.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>Embedding vectors in the same order as <paramref name="texts"/>.</returns>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
