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

    /// <summary>
    /// Matches scoring below this are discarded rather than injected into the prompt. Set to 0
    /// to keep whatever the search returns.
    /// <para>
    /// Without a floor, an out-of-scope question still gets the three closest chunks in the
    /// corpus, and the model then tries to answer from passages that have nothing to do with it.
    /// The default is derived from a measurement over this corpus with the default embedding
    /// model: the worst genuinely in-scope question scored 0.632 and the best out-of-scope one
    /// 0.459, so 0.55 sits in the gap. Both halves of that sentence matter — the number belongs
    /// to a model and a corpus, not to RAG in general, and it has to be re-measured when either
    /// changes. Run the "retrieve" console mode to do so.
    /// </para>
    /// </summary>
    public double MinimumSimilarity { get; set; } = 0.55;

    /// <summary>How many chunks are embedded per request to the model server.</summary>
    public int EmbeddingBatchSize { get; set; } = 16;

    /// <summary>
    /// Prefix prepended to every document before it is embedded.
    /// <para>
    /// Whether a prefix is needed is a property of the model. nomic-embed-text is trained with
    /// task prefixes and expects stored passages and search queries to be marked differently;
    /// leaving them off measurably degrades its ranking. bge-m3, the current default, needs no
    /// prefix, so both values are empty. Getting this wrong is silent - retrieval simply gets
    /// worse - which is why the defaults ship matched to the default model.
    /// </para>
    /// </summary>
    public string DocumentEmbeddingPrefix { get; set; } = "search_document: ";

    /// <summary>Prefix prepended to the user's question before it is embedded.</summary>
    public string QueryEmbeddingPrefix { get; set; } = "search_query: ";

    /// <summary>
    /// Which <c>IVectorStore</c> implementation to use: <c>InMemory</c> (default, no
    /// infrastructure required) or <c>Qdrant</c> (needs the container from docker-compose.yml).
    /// </summary>
    public VectorStoreKind VectorStore { get; set; } = VectorStoreKind.InMemory;
}
