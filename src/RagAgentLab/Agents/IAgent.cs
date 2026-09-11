namespace RagAgentLab.Agents;

/// <summary>An assistant that can decide to call tools before answering.</summary>
public interface IAgent
{
    /// <summary>Answers a question, calling whichever tools the model decides it needs.</summary>
    /// <param name="question">The user's question.</param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>The final answer together with the tool trace.</returns>
    Task<AgentResult> RunAsync(string question, CancellationToken cancellationToken = default);
}
