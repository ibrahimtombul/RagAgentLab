namespace RagAgentLab.Configuration;

/// <summary>
/// Settings for the Qdrant vector store, bound from the "Qdrant" section of appsettings.json.
/// Only used when <see cref="RagOptions.VectorStore"/> is set to <c>Qdrant</c>.
/// </summary>
public sealed class QdrantOptions
{
    /// <summary>Configuration section name this class is bound from.</summary>
    public const string SectionName = "Qdrant";

    /// <summary>Host name of the Qdrant server.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>gRPC port; Qdrant's default is 6334 (6333 is the REST port).</summary>
    public int Port { get; set; } = 6334;

    /// <summary>Whether to connect over TLS. Left off for a local container.</summary>
    public bool UseHttps { get; set; }

    /// <summary>API key, when the server requires one. Empty for a local container.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Collection the chunks are written to and searched in.</summary>
    public string CollectionName { get; set; } = "hr-policies";

    /// <summary>
    /// Dimension of the stored vectors. Must match the embedding model: bge-m3 produces 1024,
    /// nomic-embed-text 768. The collection is created with this size, so changing the embedding
    /// model means recreating the collection.
    /// </summary>
    public int VectorSize { get; set; } = 1024;
}
