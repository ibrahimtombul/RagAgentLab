# RagAgentLab

A hands-on **RAG (Retrieval-Augmented Generation) + agentic workflow** demo built with **C# / .NET 8**,
running entirely on a **local LLM via [Ollama](https://ollama.com)** — no API keys, no cloud costs, no data
leaving the machine.

The project is intentionally built in stages so each layer can be read (and discussed) on its own:

| Stage | Scope | Status |
|-------|-------------------------------------------------------------|--------|
| 1 | Configuration + typed Ollama client + connectivity check | ✅ done |
| 2 | RAG: chunking → embeddings → in-memory vector store → retrieval → grounded answer | ⬜ planned |
| 3 | Agentic workflow: tool definitions, tool selection by the LLM, step-by-step console trace | ⬜ planned |
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
dotnet run --project src/RagAgentLab
```

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
├── Embeddings/      # embedding generation + vector store (stage 2)
├── Rag/             # chunking, retrieval, RAG pipeline (stage 2)
├── Agents/          # agent loop and orchestration (stage 3)
├── Tools/           # tools the agent may call (stage 3)
├── Infrastructure/  # console output helpers
└── data/            # sample documents for retrieval (stage 2)
```

## Architecture notes

*(expanded in stage 4 — why Semantic Kernel, why Ollama, why an in-memory vector store)*

**Why a local model (Ollama)?** The demo must be runnable by anyone who clones the repository, without
an API key or a bill. Ollama exposes an OpenAI-compatible `/v1/chat/completions` endpoint, so the wire
format used here is the same one a cloud provider would expect — switching to Azure OpenAI is a change
of base URL and credentials, not a change of code.

**Why an interface in front of the model server?** `IOllamaClient` keeps the RAG and agent layers free of
`HttpClient` details and makes them testable with a fake implementation.
