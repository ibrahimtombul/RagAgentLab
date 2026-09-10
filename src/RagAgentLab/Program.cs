using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;
using RagAgentLab.Infrastructure;
using RagAgentLab.Ollama;

// Stage 1 - connectivity check.
// Goal: prove that the application can reach the local Ollama server, that the
// configured models are pulled, and that a completion round-trip works. Every later
// stage (RAG, agent) builds on exactly this client and this configuration pipeline.

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // Content root is pinned to the binary folder so appsettings.json and data/ are
    // found regardless of the directory the app is started from.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services
    .AddOptions<OllamaOptions>()
    .Bind(builder.Configuration.GetSection(OllamaOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Endpoint) && Uri.IsWellFormedUriString(o.Endpoint, UriKind.Absolute),
        "Ollama:Endpoint must be an absolute URI.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ChatModel), "Ollama:ChatModel must be set.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.EmbeddingModel), "Ollama:EmbeddingModel must be set.")
    .ValidateOnStart();

builder.Services
    .AddOptions<RagOptions>()
    .Bind(builder.Configuration.GetSection(RagOptions.SectionName));

// Typed HttpClient: base address and timeout come from configuration, never from code.
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>(static (serviceProvider, httpClient) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
    httpClient.BaseAddress = new Uri(options.Endpoint);
    httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

using var host = builder.Build();

var ollamaOptions = host.Services.GetRequiredService<IOptions<OllamaOptions>>().Value;
var ollama = host.Services.GetRequiredService<IOllamaClient>();

ConsoleUi.Section("RagAgentLab - Stage 1: Ollama connectivity");
ConsoleUi.Info($"Endpoint        : {ollamaOptions.Endpoint}");
ConsoleUi.Info($"Chat model      : {ollamaOptions.ChatModel}");
ConsoleUi.Info($"Embedding model : {ollamaOptions.EmbeddingModel}");

try
{
    var models = await ollama.ListModelsAsync();
    ConsoleUi.Success($"Server reachable. {models.Count} model(s) pulled locally.");
    foreach (var model in models)
    {
        ConsoleUi.Info($"  - {model}");
    }

    WarnIfMissing(models, ollamaOptions.ChatModel);
    WarnIfMissing(models, ollamaOptions.EmbeddingModel);

    ConsoleUi.Section("Test completion");
    var answer = await ollama.ChatAsync(
    [
        OllamaChatMessage.System("You are a concise assistant. Answer in one short sentence."),
        OllamaChatMessage.User("Merhaba! Kisaca kendini tanit."),
    ]);

    ConsoleUi.Answer(answer);
    ConsoleUi.Success("Stage 1 complete.");
    return 0;
}
catch (OllamaException ex)
{
    ConsoleUi.Error(ex.Message);
    ConsoleUi.Warn("Setup: brew install ollama && ollama serve");
    ConsoleUi.Warn($"Then : ollama pull {ollamaOptions.ChatModel} && ollama pull {ollamaOptions.EmbeddingModel}");
    return 1;
}

// Ollama reports models as "name:tag"; the configuration usually omits the tag.
static void WarnIfMissing(IReadOnlyList<string> pulledModels, string requiredModel)
{
    var isPulled = pulledModels.Any(m =>
        m.Equals(requiredModel, StringComparison.OrdinalIgnoreCase) ||
        m.StartsWith(requiredModel + ":", StringComparison.OrdinalIgnoreCase));

    if (!isPulled)
    {
        ConsoleUi.Warn($"Model '{requiredModel}' is not pulled. Run: ollama pull {requiredModel}");
    }
}
