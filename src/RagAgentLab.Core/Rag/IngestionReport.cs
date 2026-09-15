namespace RagAgentLab.Rag;

/// <summary>Summary of one knowledge-base ingestion run, printed by the console demo.</summary>
/// <param name="DocumentCount">Number of source files processed.</param>
/// <param name="ChunkCount">Number of chunks produced and stored.</param>
/// <param name="EmbeddingDimensions">Dimension of the vectors the embedding model returned.</param>
/// <param name="Duration">Wall-clock time the ingestion took.</param>
public sealed record IngestionReport(
    int DocumentCount,
    int ChunkCount,
    int EmbeddingDimensions,
    TimeSpan Duration);
