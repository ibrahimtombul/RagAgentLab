namespace RagAgentLab.Ollama;

/// <summary>
/// Raised when the local Ollama server cannot be reached or answers with an error.
/// Wrapping transport failures in one domain exception keeps the console layer's
/// error handling simple and lets it print an actionable hint to the user.
/// </summary>
public sealed class OllamaException : Exception
{
    /// <summary>Creates a new instance with a message.</summary>
    public OllamaException(string message) : base(message) { }

    /// <summary>Creates a new instance with a message and the underlying failure.</summary>
    public OllamaException(string message, Exception innerException) : base(message, innerException) { }
}
