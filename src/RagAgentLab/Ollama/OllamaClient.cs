using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;
using RagAgentLab.Ollama.Contracts;

namespace RagAgentLab.Ollama;

/// <summary>
/// <see cref="IOllamaClient"/> implementation that talks to Ollama over HTTP.
/// Chat goes through Ollama's OpenAI-compatible route (<c>/v1/chat/completions</c>);
/// model listing uses Ollama's native <c>/api/tags</c> route, which has no OpenAI
/// equivalent. The <see cref="HttpClient"/> is injected by IHttpClientFactory so
/// connection pooling and timeouts are handled by the framework.
/// </summary>
public sealed class OllamaClient : IOllamaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaClient> _logger;

    /// <summary>Creates a new client. Called by the DI container.</summary>
    public OllamaClient(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetFromJsonAsync<ListModelsResponse>(
                "/api/tags", JsonOptions, cancellationToken);

            return response?.Models.Select(m => m.Name).ToArray() ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new OllamaException(
                $"Ollama server at '{_options.Endpoint}' is not reachable. Is 'ollama serve' running?", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string> ChatAsync(
        IReadOnlyList<OllamaChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0)
        {
            throw new ArgumentException("At least one message is required.", nameof(messages));
        }

        var request = new ChatCompletionRequest
        {
            Model = _options.ChatModel,
            Temperature = _options.Temperature,
            Stream = false,
            Messages = messages
                .Select(m => new ChatMessageDto { Role = m.Role, Content = m.Content })
                .ToArray(),
        };

        _logger.LogDebug("Sending {Count} message(s) to model {Model}.", messages.Count, _options.ChatModel);

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.PostAsJsonAsync(
                "/v1/chat/completions", request, JsonOptions, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new OllamaException(
                $"Chat request to '{_options.Endpoint}' failed. Is 'ollama serve' running?", ex);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new OllamaException(
                $"Ollama returned {(int)httpResponse.StatusCode} for model '{_options.ChatModel}': {body}");
        }

        var completion = await httpResponse.Content
            .ReadFromJsonAsync<ChatCompletionResponse>(JsonOptions, cancellationToken);

        var content = completion?.Choices.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new OllamaException("Ollama returned an empty completion.");
        }

        if (completion?.Usage is { } usage)
        {
            _logger.LogDebug(
                "Token usage - prompt: {Prompt}, completion: {Completion}.",
                usage.PromptTokens, usage.CompletionTokens);
        }

        return content.Trim();
    }
}
