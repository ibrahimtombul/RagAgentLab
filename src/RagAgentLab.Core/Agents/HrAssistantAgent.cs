using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using RagAgentLab.Configuration;
using RagAgentLab.Rag;

namespace RagAgentLab.Agents;

/// <summary>
/// A tool-calling agent over the HR knowledge base.
/// <para>
/// The difference from the stage 2 pipeline is who is in control. In the pipeline the code
/// decides the order of operations: retrieve, then generate, always. Here the model is given
/// a set of tools and decides for itself which ones to call, in what order, and how many
/// times — the kernel runs that loop until the model stops asking for tools and produces an
/// answer. That is what <c>FunctionChoiceBehavior.Auto()</c> switches on.
/// </para>
/// <para>
/// The trade-off is worth stating plainly: the agent is more flexible but less predictable
/// and slower (every tool call is another round-trip to the model), and a small local model
/// will sometimes pick the wrong tool. For a question that always needs the same three steps,
/// the deterministic pipeline is still the better engineering choice.
/// </para>
/// </summary>
public sealed class HrAssistantAgent : IAgent
{
    // Written for a small local model: short, imperative, one rule per line.
    //
    // The language rule pins Turkish rather than saying "the user's language". qwen2.5 is a
    // Chinese-trained model and drifts back to Chinese mid-sentence when the instruction is
    // that vague and the context is thin - an answer that began in Turkish and finished in
    // Chinese is what prompted this change.
    //
    // Rule 6 names the file extension because the model was observed generalising "cite in
    // square brackets" to tool names, answering "let's check the policies. [hr-search_hr_policy]"
    // without calling anything. The wording was tightened rather than a rule added, because
    // prompt length is not a free parameter here. Adding a single four-line rule about the
    // order in which tools should be chained made qwen2.5:7b stop calling tools altogether on
    // one of the demo questions — the endpoint returned an empty message with
    // finish_reason "stop" instead of a tool call. Guidance about how a specific tool should
    // be used therefore lives in that tool's own description, which is part of the schema the
    // model is given, rather than being piled into this prompt.
    private const string SystemPrompt =
        """
        You are the assistant of Kuzey Yazılım A.Ş. You answer questions about company
        policy and about the company's online shop — products, stock and sales.

        Rules:
        1. Questions about company rules, limits, amounts or deadlines: call search_hr_policy.
           Questions about products, stock or sales: call the matching shop tool.
           Copy the user's question into the tool exactly, character for character, in its
           original language. Never translate or rewrite it.
        2. Any arithmetic: call calculate. Never compute numbers yourself.
        3. Dates and working-day deadlines: call get_today and add_business_days.
        4. Answer only after the tools have returned, using their output.
        5. Always reply in Turkish, as plain text. Never write a tool call as text.
        6. Cite the source file name — always ending in .txt — in square brackets, never a
           tool name. If the tools did not answer, say so rather than guessing.
        7. Keep the answer to a few sentences.
        """;

    /// <summary>How many recent conversation messages are kept in the prompt.</summary>
    private const int MaxConversationMessages = 6;

    /// <summary>Shown when the model returns nothing at all, instead of a blank answer.</summary>
    private const string EmptyResponseNotice =
        "[the model returned an empty response and called no tool - " +
        "try rephrasing the question, or use a larger model]";

    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly OllamaOptions _options;
    private readonly ILogger<HrAssistantAgent> _logger;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public HrAssistantAgent(
        Kernel kernel,
        IChatCompletionService chatCompletionService,
        IOptions<OllamaOptions> options,
        ILogger<HrAssistantAgent> logger)
    {
        _kernel = kernel;
        _chatCompletionService = chatCompletionService;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AgentResult> RunAsync(
        string question,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var stopwatch = Stopwatch.StartNew();
        using var trace = ToolCallTraceFilter.BeginScope(
            options?.OnToolCallStarted,
            options?.OnToolCallCompleted);
        using var retrievalTrace = RetrievalTrace.BeginScope(options?.OnRetrieval);

        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);
        history.AddUserMessage(question);

        var reply = await _chatCompletionService.GetChatMessageContentAsync(
            history, CreateExecutionSettings(), _kernel, cancellationToken);

        stopwatch.Stop();
        _logger.LogDebug("Agent answered after {Steps} tool call(s).", trace.Steps.Count);

        var answer = reply.Content?.Trim() ?? string.Empty;

        // A local model occasionally returns an empty message with no tool call at all
        // (finish_reason "stop" and a null content). Returning that as the answer would
        // surface as a blank line and look like a bug in this code, so the dead end is named.
        if (answer.Length == 0 && trace.Steps.Count == 0)
        {
            answer = EmptyResponseNotice;
        }
        else if (trace.Steps.Count == 0 &&
                 AgentOutputHeuristics.LooksLikeTextualToolCall(answer, ToolNames))
        {
            answer += AgentOutputHeuristics.TextualToolCallNotice;
        }

        return new AgentResult(question, answer, trace.Steps, stopwatch.Elapsed);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> RunStreamingAsync(
        IReadOnlyList<ConversationMessage> conversation,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation.Count == 0)
        {
            throw new ArgumentException("The conversation must contain at least one message.", nameof(conversation));
        }

        using var trace = ToolCallTraceFilter.BeginScope(
            options?.OnToolCallStarted,
            options?.OnToolCallCompleted);
        using var retrievalTrace = RetrievalTrace.BeginScope(options?.OnRetrieval);

        var history = BuildHistory(conversation);
        var answer = new StringBuilder();

        var stream = _chatCompletionService.GetStreamingChatMessageContentsAsync(
            history, CreateExecutionSettings(), _kernel, cancellationToken);

        await foreach (var fragment in stream.WithCancellation(cancellationToken))
        {
            if (string.IsNullOrEmpty(fragment.Content))
            {
                continue;
            }

            answer.Append(fragment.Content);
            yield return fragment.Content;
        }

        // Same dead ends as the non-streaming path. They can only be judged once the whole
        // reply has arrived, so the notice is streamed as a final fragment.
        if (trace.Steps.Count > 0)
        {
            yield break;
        }

        if (answer.Length == 0)
        {
            yield return EmptyResponseNotice;
        }
        else if (AgentOutputHeuristics.LooksLikeTextualToolCall(answer.ToString(), ToolNames))
        {
            yield return AgentOutputHeuristics.TextualToolCallNotice;
        }
    }

    /// <summary>
    /// Names of the tools registered on the kernel, in both the bare form and the
    /// <c>plugin-function</c> form the model sees in the schema, since it echoes either.
    /// </summary>
    private IReadOnlyList<string> ToolNames => _kernel.Plugins
        .SelectMany(plugin => plugin.SelectMany(function => new[]
        {
            function.Name,
            $"{plugin.Name}-{function.Name}",
        }))
        .ToArray();

    /// <summary>
    /// Execution settings for every agent call. <c>FunctionChoiceBehavior.Auto()</c> is what
    /// hands the tool schemas to the model and lets the kernel run the call loop.
    /// </summary>
    private OpenAIPromptExecutionSettings CreateExecutionSettings() => new()
    {
        Temperature = _options.Temperature,
        MaxTokens = _options.MaxOutputTokens,
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
    };

    /// <summary>
    /// Builds the chat history sent to the model: the system prompt plus the most recent turns.
    /// <para>
    /// Older turns are dropped rather than summarised. A local model's context window is small
    /// and the retrieved policy excerpts already consume a large share of it, so an unbounded
    /// history would silently push the system prompt — and with it the tool instructions — out
    /// of the window.
    /// </para>
    /// </summary>
    private ChatHistory BuildHistory(IReadOnlyList<ConversationMessage> conversation)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);

        foreach (var message in conversation.TakeLast(MaxConversationMessages))
        {
            if (message.Role == ConversationRole.User)
            {
                history.AddUserMessage(message.Content);
            }
            else
            {
                history.AddAssistantMessage(message.Content);
            }
        }

        return history;
    }
}
