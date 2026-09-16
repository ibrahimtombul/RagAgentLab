using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using RagAgentLab.Configuration;
using RagAgentLab.Embeddings;
using RagAgentLab.Rag;

namespace RagAgentLab.Tests;

/// <summary>
/// Tests for the relevance floor. Retrieval always returns the nearest chunks it has, so a
/// question the corpus cannot answer still comes back with matches; these pin down the rule that
/// turns "nearest" into "close enough".
/// </summary>
public sealed class RagPipelineRetrievalTests
{
    private static RagPipeline CreatePipeline(double minimumSimilarity, params double[] scores)
    {
        var store = new StubVectorStore(scores);

        return new RagPipeline(
            new StubEmbeddingService(),
            store,
            new UnusedChatCompletionService(),
            Options.Create(new RagOptions { TopK = scores.Length, MinimumSimilarity = minimumSimilarity }),
            Options.Create(new OllamaOptions()),
            NullLogger<RagPipeline>.Instance);
    }

    [Fact]
    public async Task RetrieveAsync_DropsMatchesBelowTheFloor()
    {
        var pipeline = CreatePipeline(0.55, 0.73, 0.61, 0.42);

        var hits = await pipeline.RetrieveAsync("soru");

        Assert.Equal(2, hits.Count);
        Assert.All(hits, hit => Assert.True(hit.Score >= 0.55));
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsNothingWhenEverythingIsTooFar()
    {
        // The out-of-scope case: real scores measured for questions this corpus cannot answer.
        var pipeline = CreatePipeline(0.55, 0.459, 0.442, 0.413);

        Assert.Empty(await pipeline.RetrieveAsync("Python'da liste nasıl sıralanır?"));
    }

    [Fact]
    public async Task RetrieveAsync_KeepsEverythingWhenTheFloorIsZero()
    {
        var pipeline = CreatePipeline(0, 0.73, 0.42, 0.11);

        Assert.Equal(3, (await pipeline.RetrieveAsync("soru")).Count);
    }

    [Fact]
    public async Task RetrieveAsync_ReportsHowManyWereDiscarded()
    {
        var pipeline = CreatePipeline(0.55, 0.73, 0.41, 0.40);
        RetrievalRecord? reported = null;

        using (RetrievalTrace.BeginScope(record => reported = record))
        {
            await pipeline.RetrieveAsync("soru");
        }

        Assert.NotNull(reported);
        Assert.Single(reported!.Hits);
        Assert.Equal(2, reported.DiscardedBelowThreshold);
    }

    private sealed class StubEmbeddingService : IEmbeddingService
    {
        public string ModelId => "stub";

        public Task<ReadOnlyMemory<float>> EmbedQueryAsync(string query, CancellationToken cancellationToken = default) =>
            Task.FromResult<ReadOnlyMemory<float>>(new[] { 1f, 0f });

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedDocumentsAsync(
            IReadOnlyList<string> documents,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(
                documents.Select(_ => (ReadOnlyMemory<float>)new[] { 1f, 0f }).ToArray());
    }

    /// <summary>Returns hits with exactly the scores a test asks for.</summary>
    private sealed class StubVectorStore : IVectorStore
    {
        private readonly double[] _scores;

        public StubVectorStore(double[] scores) => _scores = scores;

        public Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<SearchResult>> SearchAsync(
            ReadOnlyMemory<float> queryVector,
            int topK,
            ChunkOrigin? origin = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SearchResult>>(_scores
                .Take(topK)
                .Select((score, index) => new SearchResult(
                    new DocumentChunk($"c{index}", "test.txt", "Test", index, $"metin {index}"), score))
                .ToArray());

        public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_scores.Length);
    }

    /// <summary>Retrieval never generates, so any call here is a bug in the test or the code.</summary>
    private sealed class UnusedChatCompletionService : IChatCompletionService
    {
        public IReadOnlyDictionary<string, object?> Attributes => new Dictionary<string, object?>();

        public Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Retrieval must not call the chat model.");

        public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
            ChatHistory chatHistory,
            PromptExecutionSettings? executionSettings = null,
            Kernel? kernel = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Retrieval must not call the chat model.");
    }
}
