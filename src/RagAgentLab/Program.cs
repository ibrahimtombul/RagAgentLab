using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RagAgentLab.Demos;
using RagAgentLab.Infrastructure;
using RagAgentLab.Ollama;

// Entry point. All wiring lives in ServiceCollectionExtensions.AddRagAgentLab, so this file
// only picks which demo to run:
//   dotnet run --project src/RagAgentLab -- connect   (stage 1: connectivity check)
//   dotnet run --project src/RagAgentLab -- chunks    (stage 2: chunking only, no LLM needed)
//   dotnet run --project src/RagAgentLab -- rag       (stage 2: RAG pipeline)
//   dotnet run --project src/RagAgentLab -- agent     (stage 3: tool-calling agent, default)

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    // Content root is pinned to the binary folder so appsettings.json and data/ are found
    // regardless of the directory the app is started from.
    ContentRootPath = AppContext.BaseDirectory,
});

builder.Services.AddRagAgentLab(builder.Configuration, LoggerFactory.Create(logging =>
    logging.AddConfiguration(builder.Configuration.GetSection("Logging")).AddConsole()));
builder.Services.AddSingleton<ConnectivityDemo>();
builder.Services.AddSingleton<RagDemo>();
builder.Services.AddSingleton<ChunkInspectionDemo>();
builder.Services.AddSingleton<AgentDemo>();

using var host = builder.Build();

// Ctrl+C cancels the in-flight model call instead of killing the process mid-request.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var demo = args.FirstOrDefault()?.ToLowerInvariant() ?? "agent";

try
{
    switch (demo)
    {
        case "connect":
            await host.Services.GetRequiredService<ConnectivityDemo>().RunAsync(cancellation.Token);
            break;

        case "rag":
            await host.Services.GetRequiredService<RagDemo>().RunAsync(cancellation.Token);
            break;

        case "agent":
            // An optional second argument asks a single question instead of the scripted set.
            await host.Services.GetRequiredService<AgentDemo>()
                .RunAsync(args.Skip(1).FirstOrDefault(), cancellation.Token);
            break;

        case "chunks":
            await host.Services.GetRequiredService<ChunkInspectionDemo>().RunAsync(cancellation.Token);
            break;

        default:
            ConsoleUi.Error($"Unknown demo '{demo}'. Available: connect, chunks, rag, agent");
            return 2;
    }

    ConsoleUi.Success("Done.");
    return 0;
}
catch (OperationCanceledException)
{
    ConsoleUi.Warn("Cancelled.");
    return 130;
}
catch (OllamaException ex)
{
    ConsoleUi.Error(ex.Message);
    ConsoleUi.Warn("Setup: brew install ollama && ollama serve");
    ConsoleUi.Warn("Then : ollama pull llama3.2 && ollama pull nomic-embed-text");
    return 1;
}
catch (HttpRequestException ex)
{
    // Semantic Kernel surfaces transport failures as HttpRequestException rather than
    // wrapping them, so the hint is repeated here for the SDK-driven code paths.
    ConsoleUi.Error($"The model server could not be reached: {ex.Message}");
    ConsoleUi.Warn("Is 'ollama serve' running on the endpoint configured in appsettings.json?");
    return 1;
}
