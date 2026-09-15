namespace RagAgentLab.Rag;

/// <summary>Summary of one knowledge-base ingestion run, printed by the console demo.</summary>
/// <param name="DocumentCount">Number of source files processed.</param>
/// <param name="ChunkCount">Number of chunks produced and stored.</param>
/// <param name="EmbeddedChunkCount">How many of those had to be embedded on this run.</param>
/// <param name="RemovedChunkCount">How many stale chunks were deleted from the store.</param>
/// <param name="EmbeddingDimensions">Dimension of the vectors the embedding model returned.</param>
/// <param name="Duration">Wall-clock time the ingestion took.</param>
public sealed record IngestionReport(
    int DocumentCount,
    int ChunkCount,
    int EmbeddedChunkCount,
    int RemovedChunkCount,
    int EmbeddingDimensions,
    TimeSpan Duration)
{
    /// <summary>Chunks whose stored vectors were reused because their text had not changed.</summary>
    public int ReusedChunkCount => ChunkCount - EmbeddedChunkCount;
}
