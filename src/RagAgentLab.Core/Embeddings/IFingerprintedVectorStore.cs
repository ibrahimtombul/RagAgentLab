namespace RagAgentLab.Embeddings;

/// <summary>
/// Implemented by stores that survive a restart and can therefore say what they already hold.
/// <para>
/// Embedding is the slow, and in a hosted setting the expensive, part of ingestion. Once the
/// vectors outlive the process there is no reason to recompute them for documents that have
/// not changed, so the ingestor asks the store for a fingerprint per chunk and only embeds
/// what is new or edited. Stores that lose everything on restart simply do not implement this.
/// </para>
/// </summary>
public interface IFingerprintedVectorStore
{
    /// <summary>Returns the stored fingerprint of every chunk, keyed by chunk id.</summary>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    Task<IReadOnlyDictionary<string, string>> GetFingerprintsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every stored chunk, whatever its origin.
    /// <para>
    /// Needed so that chunks with no document behind them — notes taught through the chat — can
    /// still be re-embedded when the embedding model changes. Without this they would keep
    /// vectors of the previous model's width and the next search would fail on a dimension
    /// mismatch.
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes chunks that came from a document but are no longer produced by one, leaving
    /// runtime-added notes untouched.
    /// </summary>
    /// <param name="chunkIdsToKeep">File chunk ids that should survive.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>How many chunks were removed.</returns>
    Task<int> RemoveChunksNotInAsync(
        IReadOnlyCollection<string> chunkIdsToKeep,
        CancellationToken cancellationToken = default);
}
