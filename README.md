# RagAgentLab

A hands-on **RAG (Retrieval-Augmented Generation) + agentic workflow** demo built with **C# / .NET 8**,
running entirely on a **local LLM via [Ollama](https://ollama.com)** — no API keys, no cloud costs, no data
leaving the machine.

The project answers questions about the internal HR policies of a fictional company, first with a
classic RAG pipeline and then with an agent that decides for itself which tools to call.

```
== Stage 3 - Agent with tools ==
  Tools available to the model: search_hr_policy, calculate, get_today, add_business_days

  -> Question: Yurt dışından yılda en fazla kaç iş günü çalışabilirim?
  -> tool call #1: hr.search_hr_policy(question: "Yurt dışından yılda en fazla kaç iş günü çalışabilirim?")
  ->         result: --- source: 02-uzaktan-calisma-politikasi.txt (similarity 0,726) --- ... [66 ms]

  ANSWER
  Yurt dışından yılda en fazla 20 iş günü çalışabilirsiniz. Bu talep en az 15 gün öncesinden
  İK'ye bildirilmeli ve vergi/mevzuat değerlendirmesi için Mali İşler biriminin onayına sunulmalıdır.

  (1 tool call(s), 16,2s)

  -> Question: Aylık 750 TL internet katkısı bir yılda toplam kaç TL eder?
  -> tool call #1: calculator.calculate(expression: "750 * 12")
  ->         result: 9000 [5 ms]

  ANSWER
  Aylık 750 TL internet katkısı bir yılda toplam 9000 TL eder.

  (1 tool call(s), 5,8s)
```

The model chose the policy search for one question and the calculator for the other, and never
saw the annual-leave documents it was not asked about. Deciding that is the agent's whole job.

---

## Contents

- [How it is built, in stages](#how-it-is-built-in-stages)
- [Requirements](#requirements)
- [Run](#run)
- [Configuration](#configuration)
- [Project layout](#project-layout)
- [Architecture notes](#architecture-notes)
- [Known limitations](#known-limitations)
- [Tests](#tests)

---

## How it is built, in stages

Each stage is a separate commit, and each one is runnable on its own.

| Stage | What it adds | Try it |
|---|---|---|
| 1 | Configuration, DI, a typed client for the model server, connectivity check | `dotnet run --project src/RagAgentLab -- connect` |
| 2 | RAG: chunking → embeddings → vector store → retrieval → grounded answer | `dotnet run --project src/RagAgentLab -- rag` |
| 3 | Agent: tool definitions, tool selection by the model, step-by-step trace | `dotnet run --project src/RagAgentLab -- agent` |
| 4 | Tests, CI, a second vector store implementation, documentation | `dotnet test` |

---

## Requirements

- .NET 8 SDK — the project also runs on a newer runtime thanks to `<RollForward>Major</RollForward>`
- Ollama running locally

```bash
brew install ollama          # macOS; see ollama.com for Linux and Windows
ollama serve                 # starts the server on http://localhost:11434
ollama pull nomic-embed-text # embedding model (274 MB)
ollama pull qwen2.5:7b       # chat model (4.7 GB) - see Known limitations for why not llama3.2
```

Docker is optional and only needed to run the Qdrant vector store:

```bash
docker compose up -d
```

## Run

```bash
dotnet run --project src/RagAgentLab -- agent     # stage 3: tool-calling agent (default)
dotnet run --project src/RagAgentLab -- rag       # stage 2: RAG pipeline
dotnet run --project src/RagAgentLab -- chunks    # chunking preview - runs without an LLM
dotnet run --project src/RagAgentLab -- connect   # stage 1: connectivity check
```

`rag` and `agent` end with an interactive prompt, so the knowledge base can be questioned freely.
`agent` also accepts a single question as an argument:

```bash
dotnet run --project src/RagAgentLab -- agent "Eğitim bütçem ne kadar?"
```

The corpus is four fictional HR policy documents in [`data/`](src/RagAgentLab/data): annual and
parental leave, hybrid working, travel and expense limits, and training and performance reviews.
Nothing in them is real, and the model has never seen them — which is exactly what makes the
difference between a grounded and an ungrounded answer visible.

## Configuration

Nothing about the model, the host or the retrieval parameters is hard-coded. Everything lives in
[`appsettings.json`](src/RagAgentLab/appsettings.json), bound to typed options classes and validated
at start-up:

```json
{
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "ChatModel": "qwen2.5:7b",
    "EmbeddingModel": "nomic-embed-text",
    "TimeoutSeconds": 180,
    "Temperature": 0.2,
    "MaxOutputTokens": 800
  },
  "Rag": {
    "DataDirectory": "data",
    "ChunkSize": 600,
    "ChunkOverlap": 120,
    "TopK": 3,
    "EmbeddingBatchSize": 16,
    "DocumentEmbeddingPrefix": "search_document: ",
    "QueryEmbeddingPrefix": "search_query: ",
    "VectorStore": "InMemory"
  }
}
```

Any value can be overridden by an environment variable, e.g. `Ollama__ChatModel=llama3.1:8b` or
`Rag__VectorStore=Qdrant`.

## Project layout

```
src/RagAgentLab/
├── Configuration/   typed options bound from appsettings.json
├── Ollama/          HTTP client for the model server (+ OpenAI-shaped DTOs)
├── Embeddings/      embedding service, IVectorStore, in-memory and Qdrant implementations
├── Rag/             chunking, ingestion, retrieval, the RAG pipeline
├── Agents/          the tool-calling agent and its trace filter
├── Tools/           tools the agent may call: policy search, calculator, workday maths
├── Demos/           one runnable demo per stage
├── Infrastructure/  DI composition root, console helpers, Ollama compatibility shim
└── data/            the fictional HR policy corpus
tests/RagAgentLab.Tests/   unit tests for the deterministic parts (no model server needed)
```

---

## Architecture notes

### Why a local model, and why Ollama

Anyone who clones this repository can run it without an API key or a bill, and the fictional HR
documents never leave the machine — which is the same reason a real company would consider a local
model for internal documents.

Ollama also exposes an **OpenAI-compatible** API under `/v1`, so the wire format here is the one a
cloud provider expects. Moving to Azure OpenAI is a change of base URL and credentials, not a change
of code.

### Why Semantic Kernel, and how it is pointed at Ollama

Semantic Kernel provides the piece that is genuinely tedious to write by hand: the **automatic
function-calling loop**. Turning C# methods into JSON tool schemas, feeding tool results back into
the conversation, and looping until the model produces a final answer is all handled by the SDK.
Filters ([`ToolCallTraceFilter`](src/RagAgentLab/Agents/ToolCallTraceFilter.cs)) then make that loop
observable, which is the same hook a production system would use for tracing, rate limiting, or an
approval gate in front of a destructive tool.

No custom connector was needed. Because Ollama speaks the OpenAI protocol, one `OpenAIClient` with a
rewritten endpoint drives both chat and embeddings:

```csharp
var openAiClient = new OpenAIClient(
    new ApiKeyCredential("ollama-does-not-check-this"),  // required by the SDK, ignored by Ollama
    new OpenAIClientOptions { Endpoint = new Uri(new Uri(options.Endpoint), "/v1") });

kernelBuilder.AddOpenAIChatCompletion(options.ChatModel, openAiClient);
kernelBuilder.AddOpenAIEmbeddingGenerator(options.EmbeddingModel, openAiClient);
```

### Why this vector store approach

`IVectorStore` is the abstraction; there are two implementations behind it and the choice is a
configuration value.

[`InMemoryVectorStore`](src/RagAgentLab/Embeddings/InMemoryVectorStore.cs) scores every record on
each query — an exact, brute-force k-NN scan. For a corpus of this size that is the right answer:
a few dozen chunks are scanned in microseconds, results are exact rather than approximate, and there
is no container to run before the demo works. An ANN index only starts paying for itself in the tens
of thousands of chunks.

[`QdrantVectorStore`](src/RagAgentLab/Embeddings/QdrantVectorStore.cs) is what that upgrade looks
like: an HNSW index, data that survives a restart, and chunk text carried in the point payload
because the store no longer shares the application's heap. **Nothing above the interface changed** —
not the ingestor, not the RAG pipeline, not the agent's policy tool.

The interface methods are `async` even though the in-memory implementation completes synchronously.
That is deliberate: a real store is I/O bound, and designing the signature for the eventual
implementation rather than the current one is what made the swap a one-file change.

### Two retrieval-quality fixes worth knowing about

Both were found by reading the output rather than the code, and both changed the answers measurably.

**Embedding models can be asymmetric.** `nomic-embed-text` is trained with task prefixes and expects
a stored passage (`search_document:`) to be marked differently from a query (`search_query:`).
Without them every text lands in the same narrow region of the vector space and ranking degrades.
This is why [`IEmbeddingService`](src/RagAgentLab/Embeddings/IEmbeddingService.cs) has *separate
methods* for documents and queries — a caller cannot accidentally embed a question the way a
document is embedded, which would otherwise be a silent quality bug rather than a crash.

**An isolated chunk loses its topic.** "Employees work four days a week from the office" reads as a
general rule until you know it came from the onboarding section of the remote-work policy. Each
chunk therefore carries its document title into both the embedding and the prompt
([`DocumentChunk.ToContextualText`](src/RagAgentLab/Embeddings/DocumentChunk.cs)). Before this
change the model answered "haftada 4 gün" to a question about the general office-day rule; after it,
"en az 2 gün, Salı ve Perşembe" — the correct answer.

### Chunk size is a target, not a hard cap

[`TextChunker`](src/RagAgentLab/Rag/TextChunker.cs) splits on paragraph boundaries and only falls
back to a hard character cut for a paragraph that exceeds the chunk size on its own. A coherent
paragraph that overshoots slightly beats a paragraph cut in half. A chunk is also never closed while
it is still shorter than the overlap — without that rule a short document heading became a chunk of
its own and was then repeated in full at the start of the next chunk.

### Pipeline or agent?

Stage 2 and stage 3 answer the same questions in two different ways, and the difference is *who
decides the order of operations*.

|  | RAG pipeline (stage 2) | Agent (stage 3) |
|---|---|---|
| Control flow | Fixed in code: retrieve, then generate | Chosen by the model, per question |
| Retrieval | Always runs | Only when the model asks for it |
| Cost | One embedding call + one completion | One completion per tool-calling round |
| Predictability | High — same steps every time | Lower — depends on the model's judgement |
| Good for | A known question shape | Open-ended questions needing different data |

The agent is not strictly better. For a task that always needs the same three steps, the
deterministic pipeline is still the better engineering choice: it is faster, cheaper and reproducible.
The agent earns its keep when the question shape is not known in advance — "what is 15% of 3500?"
does not need a vector search, and the agent is what makes that decision.

### Provider quirks are absorbed in one place

"OpenAI-compatible" does not mean identical. OpenAI deprecated `max_tokens` in favour of
`max_completion_tokens`, and the current SDK sends the new field — but Ollama only honours the old
one and silently ignores the new one. Measured directly against the endpoint:

```
max_tokens             -> completion_tokens=20,  finish_reason=length
max_completion_tokens  -> completion_tokens=457, finish_reason=stop
```

So the configured output limit did nothing, and a model that started rambling — a common failure
mode for small models during tool calling — generated until the request timed out.
[`OllamaCompatibilityHandler`](src/RagAgentLab/Infrastructure/OllamaCompatibilityHandler.cs) renames
the field on the way out, in the one place such quirks belong, so the rest of the code keeps using
the standard SDK.

---

## Known limitations

Everything here was measured against this project, not assumed. All of it is a property of the
model: the pipeline, the tool registration and the trace are identical whichever model runs.

### `llama3.2` (3B) is not usable as an agent in Turkish

It is fine for stage 2, where the code decides what happens and the model only has to read the
retrieved context and answer. In stage 3, where it must choose tools and emit JSON arguments, it
failed on three of four demo questions:

- it wrote the tool call as plain text instead of emitting a real tool call, so no tool ran —
  and then degenerated into repetition:
  `{"name":"hr-search_hr_policy","parameters":{"question":"Yurt şĶşiınden yılda en fazla şekilık şarkışĶşi şekilık ...`
- it corrupted Turkish inside tool arguments (`"Yurt dışından"` → `"Yurt şĶdinden"`), which then
  retrieved the wrong passage;
- it answered `750 * 1` for "750 TL per month, how much per year?".

The default configuration therefore uses `qwen2.5:7b`. Switching is one line:

```json
{ "Ollama": { "ChatModel": "llama3.1:8b" } }
```

### `qwen2.5:7b` does not chain tools

It picks the right single tool reliably, copies Turkish arguments verbatim, and gets the
arithmetic right. What it does not do is use one tool's output as another tool's input — it calls
one tool and invents the other value:

| Question | What it did | Correct answer |
|---|---|---|
| "Talebi en geç hangi tarihte girmem gerekir?" | called `add_business_days(-5)` without looking the rule up | the policy says 10 working days |
| "Bugünden 10 iş günü sonrası?" | called `add_business_days` with a made-up "today" of `2023-04-05` | should have called `get_today` first |

Moving the guidance into the tool's own schema description did not fix it. This is the honest
boundary of a 7B model on a laptop, and it is why the scripted demo questions each need one tool.
A multi-step plan needs either a larger model or an explicitly orchestrated chain — at which point
the deterministic pipeline of stage 2 is the better tool for the job.

### Prompt length is not a free parameter

Adding a single four-line rule to the agent's system prompt made `qwen2.5:7b` stop calling tools
altogether on one question. Measured directly against the endpoint with everything else identical:

```
system prompt without the extra rule  -> finish_reason=tool_calls, tool_calls=[workday-get_today]
system prompt with the extra rule     -> finish_reason=stop,       tool_calls=null, content=""
```

Guidance about a specific tool therefore lives in that tool's description, which is part of the
schema, rather than being piled into the system prompt.

### An empty completion is a real outcome

Both models occasionally return an empty message with no tool call and `finish_reason: "stop"`,
particularly for compound questions. The agent names that dead end explicitly instead of returning
a blank string that would read as a bug in the application.

### Other limits

**Ingestion runs at start-up.** The in-memory store is rebuilt on every run — a second or two for
this corpus, unacceptable for a real one. With `Rag:VectorStore` set to `Qdrant` the index
persists and ingestion becomes a separate job.

**The Qdrant implementation is compiled and wired but has not been run against a live server** —
no Docker daemon was available on the machine this was developed on. The container definition is
in [`docker-compose.yml`](docker-compose.yml).

**Retrieval is single-shot.** No re-ranking, no query rewriting, no hybrid keyword search. Those
are the obvious next steps if answer quality mattered more than legibility here.

## Tests

```bash
dotnet test
```

The suite covers the deterministic parts of the system — the chunker, cosine similarity, the vector
store, the expression parser and the agent's tools — so it needs no model server and runs in CI.
The parts that do need a model are exercised by the demos instead.
