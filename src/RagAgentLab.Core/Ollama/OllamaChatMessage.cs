namespace RagAgentLab.Ollama;

/// <summary>
/// Provider-agnostic chat message used by the rest of the application, so that
/// application code never references the OpenAI/Ollama wire DTOs directly.
/// </summary>
/// <param name="Role">One of <c>system</c>, <c>user</c> or <c>assistant</c>.</param>
/// <param name="Content">Message text.</param>
public sealed record OllamaChatMessage(string Role, string Content)
{
    /// <summary>Creates a system message (instructions / persona).</summary>
    public static OllamaChatMessage System(string content) => new("system", content);

    /// <summary>Creates a user message.</summary>
    public static OllamaChatMessage User(string content) => new("user", content);

    /// <summary>Creates an assistant message (previous model output).</summary>
    public static OllamaChatMessage Assistant(string content) => new("assistant", content);
}
