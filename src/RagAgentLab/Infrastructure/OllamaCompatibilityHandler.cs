using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;

namespace RagAgentLab.Infrastructure;

/// <summary>
/// Compatibility shim between the official OpenAI SDK and Ollama's OpenAI-compatible API.
/// <para>
/// "OpenAI-compatible" does not mean identical. OpenAI deprecated <c>max_tokens</c> in favour
/// of <c>max_completion_tokens</c> and the current SDK sends the new field; Ollama only
/// understands the old one and silently ignores the new one. The effect is subtle and
/// expensive: the configured output limit does nothing, so a model that starts rambling — a
/// common failure mode for small models during tool calling — keeps generating until the
/// request times out.
/// </para>
/// <para>
/// This handler renames the field on the way out. It is the single place where such
/// provider quirks are absorbed, so the rest of the code can keep using the standard SDK.
/// </para>
/// </summary>
public sealed class OllamaCompatibilityHandler : DelegatingHandler
{
    private const string ModernField = "max_completion_tokens";
    private const string LegacyField = "max_tokens";

    private readonly ILogger<OllamaCompatibilityHandler> _logger;

    /// <summary>Creates a new handler.</summary>
    /// <param name="logger">Logger used to report rewrites at debug level.</param>
    public OllamaCompatibilityHandler(ILogger<OllamaCompatibilityHandler> logger)
        : base(new HttpClientHandler()) => _logger = logger;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Content is not null &&
            request.RequestUri?.AbsolutePath.EndsWith("/chat/completions", StringComparison.Ordinal) == true)
        {
            var rewritten = await TryRewriteAsync(request.Content, cancellationToken);
            if (rewritten is not null)
            {
                request.Content = rewritten;
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    /// <summary>Returns rewritten content, or null when nothing needed changing.</summary>
    private async Task<HttpContent?> TryRewriteAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var json = await content.ReadAsStringAsync(cancellationToken);
        if (!json.Contains(ModernField, StringComparison.Ordinal))
        {
            return null;
        }

        // Parsed rather than string-replaced: the field name could otherwise appear inside
        // the user's own message text.
        if (JsonNode.Parse(json) is not JsonObject body || !body.TryGetPropertyValue(ModernField, out var value))
        {
            return null;
        }

        body.Remove(ModernField);
        body[LegacyField] = value?.DeepClone();

        _logger.LogDebug("Rewrote '{Modern}' to '{Legacy}' for Ollama.", ModernField, LegacyField);
        return new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
    }
}
