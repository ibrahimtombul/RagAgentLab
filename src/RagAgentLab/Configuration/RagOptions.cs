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
}
