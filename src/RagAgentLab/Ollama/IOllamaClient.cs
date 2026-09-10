namespace RagAgentLab.Ollama;

/// <summary>
/// Thin abstraction over the local Ollama server. Everything above this interface
/// (RAG pipeline, agent, tools) depends on the abstraction rather than on HttpClient,
/// which keeps those layers unit-testable with a fake implementation.
/// </summary>
public interface IOllamaClient
{
    /// <summary>Lists the models that are currently pulled on the local server.</summary>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    /// <returns>Model names such as <c>llama3.2:latest</c>.</returns>
    Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a chat conversation to the configured chat model and returns its answer.</summary>
    /// <param name="messages">Full conversation, oldest message first.</param>
    /// <param name="cancellationToken">Token used to cancel the HTTP call.</param>
    /// <returns>The assistant's reply text.</returns>
    Task<string> ChatAsync(IReadOnlyList<OllamaChatMessage> messages, CancellationToken cancellationToken = default);
}
