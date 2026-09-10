using System.Text.Json.Serialization;

namespace RagAgentLab.Ollama.Contracts;

/// <summary>
/// Request body for <c>POST /v1/chat/completions</c>. Ollama exposes an
/// OpenAI-compatible surface, so these DTOs mirror the OpenAI schema. Keeping the
/// wire format OpenAI-shaped means the same code can later point at any
/// OpenAI-compatible provider (Azure OpenAI, vLLM, OpenRouter) by changing the URL.
/// </summary>
internal sealed record ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<ChatMessageDto> Messages { get; init; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }

    /// <summary>Streaming is disabled: the console demo prints whole answers.</summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; init; }
}

/// <summary>A single chat message on the wire (<c>system</c>, <c>user</c> or <c>assistant</c>).</summary>
internal sealed record ChatMessageDto
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>Response body of <c>POST /v1/chat/completions</c>.</summary>
internal sealed record ChatCompletionResponse
{
    [JsonPropertyName("choices")]
    public List<ChatChoiceDto> Choices { get; init; } = [];

    [JsonPropertyName("usage")]
    public UsageDto? Usage { get; init; }
}

/// <summary>One candidate answer returned by the model.</summary>
internal sealed record ChatChoiceDto
{
    [JsonPropertyName("message")]
    public ChatMessageDto? Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

/// <summary>Token accounting reported by the server; handy for the console trace.</summary>
internal sealed record UsageDto
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}

/// <summary>Response body of Ollama's native <c>GET /api/tags</c> (list of pulled models).</summary>
internal sealed record ListModelsResponse
{
    [JsonPropertyName("models")]
    public List<ModelInfoDto> Models { get; init; } = [];
}

/// <summary>One locally available model.</summary>
internal sealed record ModelInfoDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
