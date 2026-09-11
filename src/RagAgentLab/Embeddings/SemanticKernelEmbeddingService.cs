using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;

namespace RagAgentLab.Embeddings;

/// <summary>
/// <see cref="IEmbeddingService"/> backed by Semantic Kernel's OpenAI connector, which
/// is pointed at the local Ollama server's OpenAI-compatible <c>/v1/embeddings</c> route.
/// This class is the only place in the solution that touches an SDK embedding type.
/// </summary>
public sealed class SemanticKernelEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly RagOptions _options;
    private readonly ILogger<SemanticKernelEmbeddingService> _logger;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public SemanticKernelEmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        IOptions<RagOptions> options,
        ILogger<SemanticKernelEmbeddingService> logger)
    {
        _generator = generator;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ReadOnlyMemory<float>> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return await _generator.GenerateVectorAsync(text, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var vectors = new List<ReadOnlyMemory<float>>(texts.Count);

        // Sent in batches rather than one request per chunk: fewer round-trips, and the
        // batch size stays configurable because a bigger batch means more memory on the
        // model server side.
        foreach (var batch in texts.Chunk(Math.Max(1, _options.EmbeddingBatchSize)))
        {
            var generated = await _generator.GenerateAsync(batch, cancellationToken: cancellationToken);
            vectors.AddRange(generated.Select(e => e.Vector));
            _logger.LogDebug("Embedded {Count} chunk(s).", batch.Length);
        }

        return vectors;
    }
}
