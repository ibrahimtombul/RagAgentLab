using RagAgentLab.Infrastructure;
using RagAgentLab.Web.Components;
using RagAgentLab.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The same composition root the console host uses: one call registers the Ollama client,
// Semantic Kernel, the RAG layer and the agent.
builder.Services.AddRagAgentLab(builder.Configuration);

builder.Services.AddSingleton<KnowledgeBaseState>();
builder.Services.AddHostedService<KnowledgeBaseWarmupService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
