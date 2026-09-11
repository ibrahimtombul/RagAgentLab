namespace RagAgentLab.Configuration;

/// <summary>
/// Settings that control document ingestion and retrieval, bound from the "Rag"
/// section of appsettings.json. Used from stage 2 onwards.
/// </summary>
public sealed class RagOptions
{
    /// <summary>Configuration section name this class is bound from.</summary>
    public const string SectionName = "Rag";

    /// <summary>Folder (relative to the application directory) that holds the source documents.</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>Target chunk size in characters.</summary>
    public int ChunkSize { get; set; } = 600;

    /// <summary>Overlap in characters between consecutive chunks, so a sentence cut in half is not lost.</summary>
    public int ChunkOverlap { get; set; } = 120;

    /// <summary>How many of the most similar chunks are injected into the prompt.</summary>
    public int TopK { get; set; } = 3;

    /// <summary>How many chunks are embedded per request to the model server.</summary>
    public int EmbeddingBatchSize { get; set; } = 16;

    /// <summary>
    /// Prefix prepended to every document before it is embedded.
    /// <para>
    /// nomic-embed-text is trained with task prefixes and expects stored passages and search
    /// queries to be marked differently. Leaving them off measurably degrades ranking, because
    /// every text ends up in the same narrow region of the vector space. Set both prefixes to
    /// an empty string for models that do not use this convention (for example
    /// all-minilm or OpenAI's text-embedding-3).
    /// </para>
    /// </summary>
    public string DocumentEmbeddingPrefix { get; set; } = "search_document: ";

    /// <summary>Prefix prepended to the user's question before it is embedded.</summary>
    public string QueryEmbeddingPrefix { get; set; } = "search_query: ";
}
