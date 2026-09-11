namespace RagAgentLab.Agents;

/// <summary>One tool invocation the agent performed while answering.</summary>
/// <param name="Order">1-based position in the trace.</param>
/// <param name="ToolName">Fully qualified tool name, e.g. <c>calculator.calculate</c>.</param>
/// <param name="Arguments">Arguments the model chose, rendered for display.</param>
/// <param name="Result">What the tool returned, truncated for display.</param>
/// <param name="Duration">How long the tool took.</param>
public sealed record AgentStep(
    int Order,
    string ToolName,
    string Arguments,
    string Result,
    TimeSpan Duration);

/// <summary>The outcome of one agent run: the final answer plus the full tool trace.</summary>
/// <param name="Question">The user's question.</param>
/// <param name="Answer">The agent's final answer.</param>
/// <param name="Steps">Tool calls in the order they happened; empty if the model answered directly.</param>
/// <param name="Duration">Wall-clock time for the whole run.</param>
public sealed record AgentResult(
    string Question,
    string Answer,
    IReadOnlyList<AgentStep> Steps,
    TimeSpan Duration);
