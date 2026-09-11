using System.Diagnostics;
using Microsoft.SemanticKernel;
using RagAgentLab.Infrastructure;

namespace RagAgentLab.Agents;

/// <summary>
/// Semantic Kernel filter that observes every tool call the agent makes.
/// <para>
/// Automatic function calling is a loop that runs inside the SDK: the model asks for a tool,
/// the kernel invokes it, feeds the result back and asks again, until the model produces a
/// final answer. Without instrumentation that loop is a black box. A filter is the supported
/// hook into it, and it is the same mechanism a production system would use for
/// OpenTelemetry spans, rate limiting or an approval gate in front of a destructive tool.
/// </para>
/// <para>
/// The collected steps are held in an <see cref="AsyncLocal{T}"/> scope so that concurrent
/// agent runs do not mix their traces.
/// </para>
/// </summary>
public sealed class ToolCallTraceFilter : IFunctionInvocationFilter
{
    private const int MaxResultPreviewLength = 220;

    private static readonly AsyncLocal<TraceScope?> CurrentScope = new();

    /// <summary>Starts collecting tool calls for the current asynchronous flow.</summary>
    /// <returns>A scope holding the steps; dispose it when the run finishes.</returns>
    public static TraceScope BeginScope()
    {
        var scope = new TraceScope(() => CurrentScope.Value = null);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <inheritdoc />
    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        var scope = CurrentScope.Value;
        if (scope is null)
        {
            // Not inside an agent run (for example the plain RAG pipeline): nothing to trace.
            await next(context);
            return;
        }

        var toolName = $"{context.Function.PluginName}.{context.Function.Name}";
        var arguments = string.Join(", ", context.Arguments.Select(a => $"{a.Key}: \"{a.Value}\""));

        ConsoleUi.Step($"tool call #{scope.NextOrder}", $"{toolName}({arguments})");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            var result = Truncate(context.Result?.ToString() ?? string.Empty);
            ConsoleUi.Step("        result", $"{result} [{stopwatch.ElapsedMilliseconds} ms]");

            scope.Add(new AgentStep(scope.NextOrder, toolName, arguments, result, stopwatch.Elapsed));
        }
    }

    /// <summary>Collapses a tool result to a single short line for the console trace.</summary>
    private static string Truncate(string value)
    {
        var singleLine = value.ReplaceLineEndings(" ").Trim();
        return singleLine.Length <= MaxResultPreviewLength
            ? singleLine
            : singleLine[..MaxResultPreviewLength] + "...";
    }

    /// <summary>Collects the tool calls of a single agent run.</summary>
    public sealed class TraceScope : IDisposable
    {
        private readonly Action _onDispose;
        private readonly List<AgentStep> _steps = [];

        internal TraceScope(Action onDispose) => _onDispose = onDispose;

        /// <summary>The tool calls recorded so far, in order.</summary>
        public IReadOnlyList<AgentStep> Steps => _steps;

        /// <summary>The 1-based order number the next recorded step will get.</summary>
        internal int NextOrder => _steps.Count + 1;

        internal void Add(AgentStep step) => _steps.Add(step);

        /// <summary>Ends the scope.</summary>
        public void Dispose() => _onDispose();
    }
}
