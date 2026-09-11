using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using RagAgentLab.Configuration;

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
    // Written for a small local model: short, imperative, one rule per line. Long prompts
    // measurably degrade tool selection at this model size.
    private const string SystemPrompt =
        """
        You are the HR assistant of Kuzey Yazılım A.Ş.

        Rules:
        1. Questions about company rules, limits, amounts or deadlines: call search_hr_policy.
           Copy the user's question into the tool exactly, character for character, in its
           original language. Never translate or rewrite it.
        2. Any arithmetic: call calculate. Never compute numbers yourself.
        3. Dates and working-day deadlines: call get_today and add_business_days.
        4. Answer only after the tools have returned, using their output.
        5. Reply as plain text in the user's language. Never write a tool call as text.
        6. Cite the policy file name in square brackets. If the tools did not answer the
           question, say so rather than guessing.
        7. Keep the answer to a few sentences.
        """;

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
    public async Task<AgentResult> RunAsync(string question, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var stopwatch = Stopwatch.StartNew();
        using var trace = ToolCallTraceFilter.BeginScope();

        var history = new ChatHistory();
        history.AddSystemMessage(SystemPrompt);
        history.AddUserMessage(question);

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = _options.Temperature,
            MaxTokens = _options.MaxOutputTokens,
            // Hands the tool schemas to the model and lets the kernel run the call loop.
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        };

        var reply = await _chatCompletionService.GetChatMessageContentAsync(
            history, settings, _kernel, cancellationToken);

        stopwatch.Stop();
        _logger.LogDebug("Agent answered after {Steps} tool call(s).", trace.Steps.Count);

        return new AgentResult(
            question,
            reply.Content?.Trim() ?? string.Empty,
            trace.Steps,
            stopwatch.Elapsed);
    }
}
