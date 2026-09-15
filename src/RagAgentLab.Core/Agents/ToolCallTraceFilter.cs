using System.Diagnostics;
using Microsoft.SemanticKernel;

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
/// The filter reports what it sees through callbacks rather than writing anywhere itself, so
/// the console host can print a trace and the web host can push the same events into a live
/// view without either of them changing the agent.
/// </para>
/// <para>
/// The collected steps are held in an <see cref="AsyncLocal{T}"/> scope so that concurrent
/// agent runs — several users in the web UI, for instance — never mix their traces.
/// </para>
/// </summary>
public sealed class ToolCallTraceFilter : IFunctionInvocationFilter
{
    private const int MaxResultPreviewLength = 220;

    private static readonly AsyncLocal<TraceScope?> CurrentScope = new();

    /// <summary>Starts collecting tool calls for the current asynchronous flow.</summary>
    /// <param name="onToolCallStarted">
    /// Invoked with the tool name and rendered arguments the moment the model asks for a call,
    /// before the tool runs. Lets a host show the decision while the tool is still working.
    /// </param>
    /// <param name="onToolCallCompleted">Invoked once the tool has returned.</param>
    /// <returns>A scope holding the steps; dispose it when the run finishes.</returns>
    public static TraceScope BeginScope(
        Action<string, string>? onToolCallStarted = null,
        Action<AgentStep>? onToolCallCompleted = null)
    {
        var scope = new TraceScope(
            onToolCallStarted,
            onToolCallCompleted,
            () => CurrentScope.Value = null);

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
        var order = scope.NextOrder;

        scope.ReportStarted(toolName, arguments);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            scope.ReportCompleted(new AgentStep(
                order,
                toolName,
                arguments,
                Truncate(context.Result?.ToString() ?? string.Empty),
                stopwatch.Elapsed));
        }
    }

    /// <summary>Collapses a tool result to a single short line suitable for a trace view.</summary>
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
        private readonly Action<string, string>? _onToolCallStarted;
        private readonly Action<AgentStep>? _onToolCallCompleted;
        private readonly Action _onDispose;
        private readonly List<AgentStep> _steps = [];

        internal TraceScope(
            Action<string, string>? onToolCallStarted,
            Action<AgentStep>? onToolCallCompleted,
            Action onDispose)
        {
            _onToolCallStarted = onToolCallStarted;
            _onToolCallCompleted = onToolCallCompleted;
            _onDispose = onDispose;
        }

        /// <summary>The tool calls recorded so far, in order.</summary>
        public IReadOnlyList<AgentStep> Steps => _steps;

        /// <summary>The 1-based order number the next recorded step will get.</summary>
        internal int NextOrder => _steps.Count + 1;

        internal void ReportStarted(string toolName, string arguments) =>
            _onToolCallStarted?.Invoke(toolName, arguments);

        internal void ReportCompleted(AgentStep step)
        {
            _steps.Add(step);
            _onToolCallCompleted?.Invoke(step);
        }

        /// <summary>Ends the scope.</summary>
        public void Dispose() => _onDispose();
    }
}
