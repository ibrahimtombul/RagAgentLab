using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using OpenAI;
using RagAgentLab.Agents;
using RagAgentLab.Configuration;
using RagAgentLab.Embeddings;
using RagAgentLab.Ollama;
using RagAgentLab.Rag;
using RagAgentLab.Tools;

namespace RagAgentLab.Infrastructure;

/// <summary>
/// Composition root: every service the application needs is registered here, so
/// <c>Program.cs</c> stays a few lines long and the wiring is reviewable in one place.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers configuration, the Ollama client, Semantic Kernel, the RAG layer and the agent.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configuration">Application configuration (appsettings.json + environment variables).</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <param name="loggerFactory">
    /// Used by the pieces that must be constructed eagerly here (the Semantic Kernel client),
    /// which therefore cannot take an injected logger.
    /// </param>
    public static IServiceCollection AddRagAgentLab(
        this IServiceCollection services,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        services
            .AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(o => Uri.IsWellFormedUriString(o.Endpoint, UriKind.Absolute),
                "Ollama:Endpoint must be an absolute URI.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.ChatModel), "Ollama:ChatModel must be set.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.EmbeddingModel), "Ollama:EmbeddingModel must be set.")
            .ValidateOnStart();

        services
            .AddOptions<RagOptions>()
            .Bind(configuration.GetSection(RagOptions.SectionName))
            .Validate(o => o.ChunkOverlap < o.ChunkSize, "Rag:ChunkOverlap must be smaller than Rag:ChunkSize.")
            .Validate(o => o.TopK > 0, "Rag:TopK must be greater than zero.")
            .ValidateOnStart();

        // Typed HttpClient for the few things the SDK does not cover (health check, model list).
        services.AddHttpClient<IOllamaClient, OllamaClient>(static (serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<OllamaOptions>>().Value;
            httpClient.BaseAddress = new Uri(options.Endpoint);
            httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        AddSemanticKernel(services, configuration, loggerFactory);

        services
            .AddOptions<QdrantOptions>()
            .Bind(configuration.GetSection(QdrantOptions.SectionName));

        services.AddSingleton<IEmbeddingService, SemanticKernelEmbeddingService>();
        AddVectorStore(services, configuration);
        services.AddSingleton<KnowledgeBaseIngestor>();
        services.AddSingleton<IRagPipeline, RagPipeline>();

        // Semantic Kernel discovers filters through DI; this one prints and records every
        // tool call the agent makes.
        services.AddSingleton<IFunctionInvocationFilter, ToolCallTraceFilter>();
        services.AddSingleton<IAgent, HrAssistantAgent>();

        return services;
    }

    /// <summary>
    /// Registers the configured <see cref="IVectorStore"/> implementation.
    /// <para>
    /// Singleton in both cases: the in-memory store <em>is</em> the database for the demo and
    /// must outlive any scope, and the Qdrant client is designed to be long-lived and shared.
    /// This method is the only place that knows which implementation is in use.
    /// </para>
    /// </summary>
    private static void AddVectorStore(IServiceCollection services, IConfiguration configuration)
    {
        var kind = configuration.GetSection(RagOptions.SectionName).Get<RagOptions>()?.VectorStore
                   ?? VectorStoreKind.InMemory;

        switch (kind)
        {
            case VectorStoreKind.Qdrant:
                services.AddSingleton<IVectorStore, QdrantVectorStore>();
                break;

            case VectorStoreKind.InMemory:
            default:
                services.AddSingleton<IVectorStore, InMemoryVectorStore>();
                break;
        }
    }

    /// <summary>
    /// Registers Semantic Kernel with its official OpenAI connector pointed at the local
    /// Ollama server.
    /// <para>
    /// Ollama serves an OpenAI-compatible API under <c>/v1</c>, so no custom connector is
    /// needed: a single <see cref="OpenAIClient"/> with a rewritten endpoint drives both chat
    /// completion and embeddings. The API key is a placeholder because Ollama ignores it, but
    /// the SDK requires a non-empty credential.
    /// </para>
    /// </summary>
    private static void AddSemanticKernel(
        IServiceCollection services,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var options = configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>() ?? new OllamaOptions();

        var compatibilityHandler = new OllamaCompatibilityHandler(
            loggerFactory.CreateLogger<OllamaCompatibilityHandler>());

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential("ollama-does-not-check-this"),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(new Uri(options.Endpoint), "/v1"),
                NetworkTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                Transport = new HttpClientPipelineTransport(new HttpClient(compatibilityHandler)
                {
                    Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
                }),
            });

        var kernelBuilder = services.AddKernel();
        kernelBuilder.AddOpenAIChatCompletion(options.ChatModel, openAiClient);

        // SKEXP0010: Semantic Kernel still marks the embedding-generator registration as
        // experimental (it moved to Microsoft.Extensions.AI recently). Suppressed here only,
        // rather than project-wide, so any other experimental API still fails the build.
#pragma warning disable SKEXP0010
        kernelBuilder.AddOpenAIEmbeddingGenerator(options.EmbeddingModel, openAiClient);
#pragma warning restore SKEXP0010

        // Tools the agent may call. The plugin name becomes the prefix the model sees
        // (for example "hr.search_hr_policy"), and constructor dependencies such as
        // IRagPipeline are resolved from this same container.
        kernelBuilder.Plugins.AddFromType<HrPolicyTool>("hr");
        kernelBuilder.Plugins.AddFromType<CalculatorTool>("calculator");
        kernelBuilder.Plugins.AddFromType<WorkdayTool>("workday");
    }
}
