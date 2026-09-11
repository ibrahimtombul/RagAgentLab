# RagAgentLab

A hands-on **RAG (Retrieval-Augmented Generation) + agentic workflow** demo built with **C# / .NET 8**,
running entirely on a **local LLM via [Ollama](https://ollama.com)** — no API keys, no cloud costs, no data
leaving the machine.

The project is intentionally built in stages so each layer can be read (and discussed) on its own:

| Stage | Scope | Status |
|-------|-------------------------------------------------------------|--------|
| 1 | Configuration + typed Ollama client + connectivity check | ✅ done |
| 2 | RAG: chunking → embeddings → in-memory vector store → retrieval → grounded answer | ✅ done |
| 3 | Agentic workflow: tool definitions, tool selection by the LLM, step-by-step console trace | ✅ done |
| 4 | Polish: folder structure, XML docs, README, architecture notes | ⬜ planned |

---

## Requirements

- .NET 8 SDK (the project also runs on a .NET 10 runtime thanks to `<RollForward>Major</RollForward>`)
- Ollama running locally

```bash
brew install ollama          # macOS
ollama serve                 # starts the server on http://localhost:11434
ollama pull llama3.2         # chat model
ollama pull nomic-embed-text # embedding model
```

## Run

```bash
dotnet run --project src/RagAgentLab -- agent     # stage 3: tool-calling agent (default)
dotnet run --project src/RagAgentLab -- rag       # stage 2: RAG pipeline
dotnet run --project src/RagAgentLab -- chunks    # chunking preview, runs without an LLM
dotnet run --project src/RagAgentLab -- connect   # stage 1: connectivity check
```

Each demo ends with an interactive prompt, so the knowledge base can be questioned freely.

## Configuration

Nothing about the model or the host is hard-coded. Everything lives in
[`appsettings.json`](src/RagAgentLab/appsettings.json) and is bound to typed options classes:

```json
{
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "ChatModel": "llama3.2",
    "EmbeddingModel": "nomic-embed-text",
    "TimeoutSeconds": 180,
    "Temperature": 0.2
  },
  "Rag": { "DataDirectory": "data", "ChunkSize": 600, "ChunkOverlap": 120, "TopK": 3 }
}
```

Any setting can be overridden by an environment variable, e.g. `Ollama__ChatModel=qwen2.5`.

## Project layout

```
src/RagAgentLab/
├── Configuration/   # typed options bound from appsettings.json
├── Ollama/          # HTTP client for the local model server (+ OpenAI-shaped DTOs)
├── Embeddings/      # embedding service, vector store abstraction + in-memory implementation
├── Rag/             # chunking, ingestion, retrieval, RAG pipeline
├── Agents/          # the tool-calling agent and its trace filter
├── Tools/           # tools the agent may call (policy search, calculator, workday maths)
├── Demos/           # one runnable demo per stage
├── Infrastructure/  # DI composition root + console output helpers
└── data/            # fictional HR policy documents used as the corpus
```

## Known limitations

Everything below is a property of the default model, not of the wiring — the pipeline, the
tool registration and the trace are the same whichever model is configured.

**`llama3.2` (3B) is not reliable as an agent in Turkish.** It is fine for stage 2, where the
code decides what happens and the model only has to read the retrieved context and answer.
In stage 3, where it must choose tools and emit JSON arguments, it fails in three ways:

- it writes the tool call as plain text instead of emitting a real tool call, so no tool runs
  (the demo detects this and says so rather than pretending the answer is valid);
- it corrupts Turkish text inside tool arguments (`"Yurt dışından"` came back as
  `"Yurt şĶdinden"`), which then retrieves the wrong passage;
- it invents numbers instead of calling `search_hr_policy` for them.

Switching to a model trained for tool use fixes this and is a one-line configuration change:

```bash
ollama pull qwen2.5:7b
```

```json
{ "Ollama": { "ChatModel": "qwen2.5:7b" } }
```

**Two Ollama quirks are handled in code rather than worked around by hand.** Ollama's
OpenAI-compatible endpoint ignores `max_completion_tokens`, the field the current OpenAI SDK
sends, and only honours the deprecated `max_tokens`; without the rewrite in
`OllamaCompatibilityHandler` the configured output limit silently does nothing and a rambling
model generates until the request times out. And `nomic-embed-text` expects the task prefixes
`search_document:` / `search_query:`; leaving them off measurably worsens ranking, which is
why `IEmbeddingService` has separate methods for documents and queries.

## Architecture notes

*(expanded in stage 4 — why Semantic Kernel, why Ollama, why an in-memory vector store)*

**Why a local model (Ollama)?** The demo must be runnable by anyone who clones the repository, without
an API key or a bill. Ollama exposes an OpenAI-compatible `/v1/chat/completions` endpoint, so the wire
format used here is the same one a cloud provider would expect — switching to Azure OpenAI is a change
of base URL and credentials, not a change of code.

**Why an interface in front of the model server?** `IOllamaClient` keeps the RAG and agent layers free of
`HttpClient` details and makes them testable with a fake implementation.
