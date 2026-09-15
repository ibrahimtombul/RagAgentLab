using RagAgentLab.Embeddings;

namespace RagAgentLab.Rag;

/// <summary>One vector search: what was looked up and what came back.</summary>
/// <param name="Query">The text that was embedded and searched with.</param>
/// <param name="Hits">The matches, best first.</param>
/// <param name="Duration">How long the embedding call plus the search took.</param>
public sealed record RetrievalRecord(
    string Query,
    IReadOnlyList<SearchResult> Hits,
    TimeSpan Duration);

/// <summary>
/// Publishes every vector search to whoever is watching the current agent run.
/// <para>
/// The tool trace already shows that <c>search_hr_policy</c> was called and prints a
/// truncated blob of what it returned, which is enough to see the agent's decision but not
/// enough to debug retrieval: which document matched, which chunk of it, and how close the
/// match actually was. Those are the numbers that explain a wrong answer, so they are
/// reported separately and in structured form rather than being flattened into the tool's
/// string result.
/// </para>
/// <para>
/// Uses the same <see cref="AsyncLocal{T}"/> scoping as the tool trace, so concurrent chat
/// sessions never see each other's searches.
/// </para>
/// </summary>
public static class RetrievalTrace
{
    private static readonly AsyncLocal<Action<RetrievalRecord>?> CurrentObserver = new();

    /// <summary>Starts reporting searches on the current asynchronous flow.</summary>
    /// <param name="onRetrieval">Called once per search; may be null to disable reporting.</param>
    /// <returns>A scope to dispose when the run finishes.</returns>
    public static IDisposable BeginScope(Action<RetrievalRecord>? onRetrieval)
    {
        CurrentObserver.Value = onRetrieval;
        return new Scope();
    }

    /// <summary>Reports one search. Does nothing when nobody is watching.</summary>
    /// <param name="record">What was searched and what matched.</param>
    public static void Report(RetrievalRecord record) => CurrentObserver.Value?.Invoke(record);

    private sealed class Scope : IDisposable
    {
        public void Dispose() => CurrentObserver.Value = null;
    }
}
