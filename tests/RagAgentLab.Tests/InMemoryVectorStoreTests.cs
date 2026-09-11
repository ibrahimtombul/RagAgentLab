using RagAgentLab.Embeddings;

namespace RagAgentLab.Tests;

/// <summary>Tests for the in-memory implementation of <see cref="IVectorStore"/>.</summary>
public sealed class InMemoryVectorStoreTests
{
    private static VectorRecord Record(string id, params float[] vector) =>
        new(new DocumentChunk(id, $"{id}.txt", "Test Document", 0, $"text of {id}"), vector.AsMemory());

    [Fact]
    public async Task SearchAsync_RanksByDescendingSimilarity()
    {
        IVectorStore store = new InMemoryVectorStore();
        await store.UpsertAsync(
        [
            Record("far", -1f, 0f),
            Record("near", 1f, 0f),
            Record("middle", 1f, 1f),
        ]);

        var hits = await store.SearchAsync(new[] { 1f, 0f }.AsMemory(), topK: 3);

        Assert.Equal(["near", "middle", "far"], hits.Select(h => h.Chunk.Id));
        Assert.True(hits[0].Score > hits[1].Score);
        Assert.True(hits[1].Score > hits[2].Score);
    }

    [Fact]
    public async Task SearchAsync_ReturnsAtMostTopK()
    {
        IVectorStore store = new InMemoryVectorStore();
        await store.UpsertAsync([Record("a", 1f, 0f), Record("b", 0f, 1f), Record("c", 1f, 1f)]);

        var hits = await store.SearchAsync(new[] { 1f, 0f }.AsMemory(), topK: 2);

        Assert.Equal(2, hits.Count);
    }

    [Fact]
    public async Task SearchAsync_ReturnsNothingWhenStoreIsEmpty()
    {
        IVectorStore store = new InMemoryVectorStore();

        Assert.Empty(await store.SearchAsync(new[] { 1f, 0f }.AsMemory(), topK: 3));
    }

    [Fact]
    public async Task UpsertAsync_ReplacesARecordWithTheSameId()
    {
        IVectorStore store = new InMemoryVectorStore();
        await store.UpsertAsync([Record("same", 1f, 0f)]);
        await store.UpsertAsync([Record("same", 0f, 1f)]);

        Assert.Equal(1, await store.CountAsync());

        var hits = await store.SearchAsync(new[] { 0f, 1f }.AsMemory(), topK: 1);
        Assert.Equal(1d, hits[0].Score, precision: 5);
    }

    [Fact]
    public async Task SearchAsync_RejectsNonPositiveTopK()
    {
        IVectorStore store = new InMemoryVectorStore();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => store.SearchAsync(new[] { 1f, 0f }.AsMemory(), topK: 0));
    }
}
