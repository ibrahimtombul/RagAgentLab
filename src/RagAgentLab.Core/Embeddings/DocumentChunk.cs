namespace RagAgentLab.Embeddings;

/// <summary>
/// A single retrievable unit of text: one slice of a source document together with
/// enough metadata to cite it back to the user.
/// </summary>
/// <param name="Id">Stable identifier, e.g. <c>01-izin-politikasi.txt#3</c>.</param>
/// <param name="SourceName">File name the chunk came from, used for citations.</param>
/// <param name="DocumentTitle">Title line of the source document, used as context for the chunk.</param>
/// <param name="ChunkIndex">Zero-based position of the chunk inside its source document.</param>
/// <param name="Text">The raw chunk text.</param>
public sealed record DocumentChunk(
    string Id,
    string SourceName,
    string DocumentTitle,
    int ChunkIndex,
    string Text)
{
    /// <summary>
    /// The chunk with its document title prepended — the form that is both embedded and
    /// injected into the prompt.
    /// <para>
    /// An isolated excerpt loses the one thing that says what it is about. "Employees work
    /// four days a week from the office" reads as a general rule until you know it came out
    /// of the onboarding section of the remote-work policy. Prepending the title costs a few
    /// tokens per chunk and fixes both halves of the problem at once: it gives the embedding
    /// a topic signal, and it tells the model which document it is reading.
    /// </para>
    /// </summary>
    /// <returns>The contextualised chunk text.</returns>
    public string ToContextualText() => $"[{SourceName} — {DocumentTitle}]\n{Text}";

    /// <summary>
    /// Where this chunk came from. Re-ingesting the data directory clears out file chunks that
    /// no longer exist, and this is what keeps it from deleting notes added from the chat
    /// along with them.
    /// </summary>
    public ChunkOrigin Origin { get; init; } = ChunkOrigin.File;
}

/// <summary>Where a chunk came from.</summary>
public enum ChunkOrigin
{
    /// <summary>Produced by ingesting a document from the data directory.</summary>
    File = 0,

    /// <summary>Added at runtime, for example a note typed into the chat.</summary>
    Note = 1,
}

/// <summary>A chunk together with its embedding vector, as held by the vector store.</summary>
/// <param name="Chunk">The chunk metadata and text.</param>
/// <param name="Vector">Embedding of <see cref="DocumentChunk.Text"/>.</param>
public sealed record VectorRecord(DocumentChunk Chunk, ReadOnlyMemory<float> Vector);

/// <summary>A search hit: a chunk plus how similar it was to the query.</summary>
/// <param name="Chunk">The matched chunk.</param>
/// <param name="Score">Cosine similarity in the range -1..1; 1 means identical direction.</param>
public sealed record SearchResult(DocumentChunk Chunk, double Score);
