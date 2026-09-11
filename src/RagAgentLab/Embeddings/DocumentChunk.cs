namespace RagAgentLab.Embeddings;

/// <summary>
/// A single retrievable unit of text: one slice of a source document together with
/// enough metadata to cite it back to the user.
/// </summary>
/// <param name="Id">Stable identifier, e.g. <c>01-izin-politikasi.txt#3</c>.</param>
/// <param name="SourceName">File name the chunk came from, used for citations.</param>
/// <param name="ChunkIndex">Zero-based position of the chunk inside its source document.</param>
/// <param name="Text">The chunk text that is embedded and later injected into the prompt.</param>
public sealed record DocumentChunk(string Id, string SourceName, int ChunkIndex, string Text);

/// <summary>A chunk together with its embedding vector, as held by the vector store.</summary>
/// <param name="Chunk">The chunk metadata and text.</param>
/// <param name="Vector">Embedding of <see cref="DocumentChunk.Text"/>.</param>
public sealed record VectorRecord(DocumentChunk Chunk, ReadOnlyMemory<float> Vector);

/// <summary>A search hit: a chunk plus how similar it was to the query.</summary>
/// <param name="Chunk">The matched chunk.</param>
/// <param name="Score">Cosine similarity in the range -1..1; 1 means identical direction.</param>
public sealed record SearchResult(DocumentChunk Chunk, double Score);
