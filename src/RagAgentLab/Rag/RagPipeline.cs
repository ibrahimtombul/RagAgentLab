using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using RagAgentLab.Configuration;
using RagAgentLab.Embeddings;

namespace RagAgentLab.Rag;

/// <summary>
/// The classic RAG loop: embed the question, find the nearest chunks, paste them into the
/// prompt as context, and let the model answer strictly from that context.
/// <para>
/// Grounding is enforced by the system prompt: the model is told to answer only from the
/// supplied context, to cite the source file of each claim, and to say plainly when the
/// answer is not in the documents. That last instruction is what keeps a small local model
/// from inventing plausible-sounding policy numbers.
/// </para>
/// </summary>
public sealed class RagPipeline : IRagPipeline
{
    private const string GroundedSystemPrompt =
        """
        You are the HR assistant of a company called Kuzey Yazılım A.Ş.
        Answer the user's question using ONLY the CONTEXT section below.

        Rules:
        - If the answer is not in the context, say so explicitly and do not guess.
        - Never invent numbers, dates, limits or policy names that are not in the context.
        - Cite the source file name in square brackets after each fact, e.g. [01-izin-politikasi.txt].
        - Answer in the same language as the question.
        - Be concise: a few sentences or a short list.
        """;

    private const string UngroundedSystemPrompt =
        """
        You are an HR assistant. Answer the user's question concisely, in the same language
        as the question.
        """;

    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly RagOptions _ragOptions;
    private readonly OllamaOptions _ollamaOptions;
    private readonly ILogger<RagPipeline> _logger;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public RagPipeline(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IChatCompletionService chatCompletionService,
        IOptions<RagOptions> ragOptions,
        IOptions<OllamaOptions> ollamaOptions,
        ILogger<RagPipeline> logger)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _chatCompletionService = chatCompletionService;
        _ragOptions = ragOptions.Value;
        _ollamaOptions = ollamaOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> RetrieveAsync(
        string question,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var queryVector = await _embeddingService.EmbedQueryAsync(question, cancellationToken);
        var hits = await _vectorStore.SearchAsync(queryVector, topK ?? _ragOptions.TopK, cancellationToken);

        _logger.LogDebug("Retrieved {Count} chunk(s) for '{Question}'.", hits.Count, question);
        return hits;
    }

    /// <inheritdoc />
    public async Task<RagAnswer> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        var stopwatch = Stopwatch.StartNew();

        var hits = await RetrieveAsync(question, cancellationToken: cancellationToken);

        var history = new ChatHistory();
        history.AddSystemMessage(GroundedSystemPrompt);
        history.AddUserMessage(BuildUserPrompt(question, hits));

        var reply = await _chatCompletionService.GetChatMessageContentAsync(
            history, CreateExecutionSettings(), kernel: null, cancellationToken);

        stopwatch.Stop();

        return new RagAnswer(
            question,
            reply.Content?.Trim() ?? string.Empty,
            hits,
            stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public async Task<string> AskWithoutContextAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var history = new ChatHistory();
        history.AddSystemMessage(UngroundedSystemPrompt);
        history.AddUserMessage(question);

        var reply = await _chatCompletionService.GetChatMessageContentAsync(
            history, CreateExecutionSettings(), kernel: null, cancellationToken);

        return reply.Content?.Trim() ?? string.Empty;
    }

    /// <summary>Renders the retrieved chunks and the question into a single user message.</summary>
    private static string BuildUserPrompt(string question, IReadOnlyList<SearchResult> hits)
    {
        var builder = new StringBuilder();
        builder.AppendLine("CONTEXT:");

        if (hits.Count == 0)
        {
            builder.AppendLine("(no relevant document found)");
        }

        foreach (var hit in hits)
        {
            builder.AppendLine($"--- source: {hit.Chunk.SourceName} ---");
            builder.AppendLine(hit.Chunk.ToContextualText());
            builder.AppendLine();
        }

        builder.AppendLine("QUESTION:");
        builder.Append(question);
        return builder.ToString();
    }

    private OpenAIPromptExecutionSettings CreateExecutionSettings() => new()
    {
        Temperature = _ollamaOptions.Temperature,
        MaxTokens = _ollamaOptions.MaxOutputTokens,
    };
}
