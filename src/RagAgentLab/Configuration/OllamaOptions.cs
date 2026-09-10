namespace RagAgentLab.Configuration;

/// <summary>
/// Strongly typed settings for the local Ollama server, bound from the "Ollama"
/// section of appsettings.json. Nothing about the model or the host is hard-coded
/// in the application code, so switching to another model (or to a remote,
/// OpenAI-compatible endpoint) is a configuration change only.
/// </summary>
public sealed class OllamaOptions
{
    /// <summary>Configuration section name this class is bound from.</summary>
    public const string SectionName = "Ollama";

    /// <summary>Base address of the Ollama server, e.g. <c>http://localhost:11434</c>.</summary>
    public string Endpoint { get; set; } = "http://localhost:11434";

    /// <summary>Model used for chat/completion calls, e.g. <c>llama3.2</c>.</summary>
    public string ChatModel { get; set; } = "llama3.2";

    /// <summary>Model used to produce embedding vectors, e.g. <c>nomic-embed-text</c>.</summary>
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>HTTP timeout in seconds. Local models on CPU can be slow, so this is generous.</summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>Sampling temperature. Kept low because RAG answers should stay close to the sources.</summary>
    public double Temperature { get; set; } = 0.2;
}
