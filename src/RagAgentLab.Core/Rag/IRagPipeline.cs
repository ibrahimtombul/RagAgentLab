using RagAgentLab.Embeddings;

namespace RagAgentLab.Rag;

/// <summary>
/// Retrieval-augmented generation over the ingested knowledge base.
/// <para>
/// Retrieval is exposed separately from the full pipeline because stage 3 registers it as a
/// tool the agent can call on its own.
/// </para>
/// </summary>
public interface IRagPipeline
{
    /// <summary>Finds the chunks most relevant to a question.</summary>
    /// <param name="question">Natural-language question.</param>
    /// <param name="topK">How many chunks to return; falls back to the configured value when null.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>Matching chunks, best match first.</returns>
    Task<IReadOnlyList<SearchResult>> RetrieveAsync(
        string question,
        int? topK = null,
        CancellationToken cancellationToken = default);

    /// <summary>Retrieves context and asks the model to answer strictly from it.</summary>
    /// <param name="question">Natural-language question.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>The answer together with the sources it was grounded in.</returns>
    Task<RagAnswer> AskAsync(string question, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the same question with no retrieved context at all. Used by the demo to show the
    /// difference grounding makes, since the model has never seen the fictional company.
    /// </summary>
    /// <param name="question">Natural-language question.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>The ungrounded answer.</returns>
    Task<string> AskWithoutContextAsync(string question, CancellationToken cancellationToken = default);
}
