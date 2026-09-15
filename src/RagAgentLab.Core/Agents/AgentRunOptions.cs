using RagAgentLab.Rag;

namespace RagAgentLab.Agents;

/// <summary>
/// Per-run options for an agent call. Currently carries the callbacks a host uses to watch
/// the tool-calling loop while it is still running, which is what lets the console print a
/// live trace and the web UI show the agent's decisions before the answer arrives.
/// </summary>
public sealed class AgentRunOptions
{
    /// <summary>Called with the tool name and rendered arguments when the model asks for a call.</summary>
    public Action<string, string>? OnToolCallStarted { get; init; }

    /// <summary>Called once a tool has returned, with the recorded step.</summary>
    public Action<AgentStep>? OnToolCallCompleted { get; init; }

    /// <summary>
    /// Called for every vector search performed during the run, with the matched chunks and
    /// their similarity scores. This is what a host needs to show why an answer came out the
    /// way it did.
    /// </summary>
    public Action<RetrievalRecord>? OnRetrieval { get; init; }
}
