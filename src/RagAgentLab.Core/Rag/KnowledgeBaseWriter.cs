using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;
using RagAgentLab.Embeddings;

namespace RagAgentLab.Rag;

/// <summary>Result of adding a note to the knowledge base.</summary>
/// <param name="SourceName">The synthetic source name the note was filed under.</param>
/// <param name="ChunkCount">How many chunks it produced.</param>
public sealed record NoteResult(string SourceName, int ChunkCount);

/// <summary>
/// Adds free text to the knowledge base at runtime, so the assistant can be taught something
/// without editing a file and restarting.
/// <para>
/// This is deliberately <em>not</em> a tool the model may call. Letting the model decide when
/// to write to the corpus means a misread instruction can poison it permanently, and this
/// model has already been measured mistaking its own tool calls for prose. Teaching is
/// therefore an explicit action the user takes, and the code path never passes through the
/// model at all.
/// </para>
/// <para>
/// Notes are stored with <see cref="ChunkOrigin.Note"/> so that re-ingesting the document
/// folder cannot sweep them away, and they only outlive the process when the configured
/// vector store is a persistent one.
/// </para>
/// </summary>
public sealed class KnowledgeBaseWriter
{
    private const string NoteTitle = "Sohbet üzerinden eklenen not";

    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly RagOptions _options;
    private readonly ILogger<KnowledgeBaseWriter> _logger;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public KnowledgeBaseWriter(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IOptions<RagOptions> options,
        ILogger<KnowledgeBaseWriter> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Chunks, embeds and stores a note.</summary>
    /// <param name="text">The text to remember.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>Where it was filed and how many chunks it became.</returns>
    /// <exception cref="ArgumentException">The note is empty or too short to be useful.</exception>
    public async Task<NoteResult> AddNoteAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var note = text.Trim();
        if (note.Length < 10)
        {
            throw new ArgumentException("Not en az 10 karakter olmalı.", nameof(text));
        }

        // Timestamped so two notes never collide, and so the source name shown in a citation
        // tells the user when they taught it.
        var sourceName = $"not-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";

        // Short notes stay a single chunk; the chunker only splits when they grow past the
        // configured size, which is the same rule documents follow.
        var pieces = note.Length <= _options.ChunkSize
            ? [note]
            : TextChunker.Split(note, _options.ChunkSize, _options.ChunkOverlap);

        var chunks = pieces
            .Select((piece, index) => new DocumentChunk(
                Id: $"{sourceName}#{index}",
                SourceName: sourceName,
                DocumentTitle: NoteTitle,
                ChunkIndex: index,
                Text: piece)
            {
                Origin = ChunkOrigin.Note,
            })
            .ToArray();

        var texts = chunks.Select(c => c.ToContextualText()).ToArray();
        var vectors = await _embeddingService.EmbedDocumentsAsync(texts, cancellationToken);

        var records = chunks
            .Select((chunk, index) => new VectorRecord(
                chunk,
                vectors[index],
                ChunkFingerprint.Compute(_embeddingService.ModelId, texts[index])))
            .ToArray();

        await _vectorStore.UpsertAsync(records, cancellationToken);

        _logger.LogInformation("Added note {Source} as {Count} chunk(s).", sourceName, chunks.Length);
        return new NoteResult(sourceName, chunks.Length);
    }
}
