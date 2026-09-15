namespace RagAgentLab.Agents;

/// <summary>An assistant that can decide to call tools before answering.</summary>
public interface IAgent
{
    /// <summary>Answers a question, calling whichever tools the model decides it needs.</summary>
    /// <param name="question">The user's question.</param>
    /// <param name="options">Optional callbacks for observing tool calls as they happen.</param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>The final answer together with the tool trace.</returns>
    Task<AgentResult> RunAsync(
        string question,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers the latest turn of a conversation, streaming the reply as it is generated.
    /// <para>
    /// Tool calls still happen inside the SDK's loop before any text is produced, so the
    /// callbacks in <paramref name="options"/> fire first and the stream then carries only the
    /// final answer. That ordering is what lets a UI show "searching the policies..." while
    /// the user waits.
    /// </para>
    /// </summary>
    /// <param name="conversation">The conversation so far, oldest first, ending with the user's new message.</param>
    /// <param name="options">Optional callbacks for observing tool calls as they happen.</param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>Fragments of the answer, in order.</returns>
    IAsyncEnumerable<string> RunStreamingAsync(
        IReadOnlyList<ConversationMessage> conversation,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default);
}
