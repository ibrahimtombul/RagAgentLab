using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;
using RagAgentLab.Infrastructure;
using RagAgentLab.Ollama;

namespace RagAgentLab.Demos;

/// <summary>
/// Stage 1 demo: proves the application can reach the local model server, that the
/// configured models are pulled, and that a completion round-trip works.
/// </summary>
public sealed class ConnectivityDemo
{
    private readonly IOllamaClient _ollama;
    private readonly OllamaOptions _options;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public ConnectivityDemo(IOllamaClient ollama, IOptions<OllamaOptions> options)
    {
        _ollama = ollama;
        _options = options.Value;
    }

    /// <summary>Runs the connectivity check.</summary>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ConsoleUi.Section("Stage 1 - Ollama connectivity");
        ConsoleUi.Info($"Endpoint        : {_options.Endpoint}");
        ConsoleUi.Info($"Chat model      : {_options.ChatModel}");
        ConsoleUi.Info($"Embedding model : {_options.EmbeddingModel}");

        var models = await _ollama.ListModelsAsync(cancellationToken);
        ConsoleUi.Success($"Server reachable. {models.Count} model(s) pulled locally.");
        foreach (var model in models)
        {
            ConsoleUi.Info($"  - {model}");
        }

        WarnIfMissing(models, _options.ChatModel);
        WarnIfMissing(models, _options.EmbeddingModel);

        ConsoleUi.Section("Test completion (raw HTTP, no SDK)");
        var answer = await _ollama.ChatAsync(
        [
            OllamaChatMessage.System("You are a concise assistant. Answer in one short sentence."),
            OllamaChatMessage.User("Merhaba! Kısaca kendini tanıt."),
        ], cancellationToken);

        ConsoleUi.Answer(answer);
    }

    /// <summary>Ollama reports models as <c>name:tag</c>; configuration usually omits the tag.</summary>
    private static void WarnIfMissing(IReadOnlyList<string> pulledModels, string requiredModel)
    {
        var isPulled = pulledModels.Any(m =>
            m.Equals(requiredModel, StringComparison.OrdinalIgnoreCase) ||
            m.StartsWith(requiredModel + ":", StringComparison.OrdinalIgnoreCase));

        if (!isPulled)
        {
            ConsoleUi.Warn($"Model '{requiredModel}' is not pulled. Run: ollama pull {requiredModel}");
        }
    }
}
