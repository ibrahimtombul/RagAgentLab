using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;
using RagAgentLab.Embeddings;

namespace RagAgentLab.Tests;

/// <summary>
/// Tests for the persistent store. Each test gets its own database file, so they exercise the
/// real SQLite round-trip rather than a substitute.
/// </summary>
public sealed class SqliteVectorStoreTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"ragagentlab-test-{Guid.NewGuid():N}.db");

    private SqliteVectorStore CreateStore() =>
        new(Options.Create(new SqliteOptions { DatabasePath = _databasePath }),
            NullLogger<SqliteVectorStore>.Instance);

    private static VectorRecord Record(
        string id,
        string text,
        ChunkOrigin origin,
        params float[] vector) =>
        new(new DocumentChunk(id, $"{id}.txt", "Test", 0, text) { Origin = origin }, vector.AsMemory());

    [Fact]
    public async Task Vectors_SurviveAcrossInstances()
    {
        var first = CreateStore();
        await first.UpsertAsync([Record("a", "metin a", ChunkOrigin.File, 1f, 0f)]);
        first.Dispose();

        // A new instance reads the same file, which is what a restart does.
        using var second = CreateStore();
        var hits = await second.SearchAsync(new[] { 1f, 0f }.AsMemory(), topK: 1);

        Assert.Equal("a", hits[0].Chunk.Id);
        Assert.Equal("metin a", hits[0].Chunk.Text);
        Assert.Equal(1d, hits[0].Score, precision: 5);
    }

    [Fact]
    public async Task Origin_SurvivesARoundTrip()
    {
        var first = CreateStore();
        await first.UpsertAsync([Record("note", "bir not", ChunkOrigin.Note, 1f, 0f)]);
        first.Dispose();

        using var second = CreateStore();
        var hits = await second.SearchAsync(new[] { 1f, 0f }.AsMemory(), topK: 1);

        Assert.Equal(ChunkOrigin.Note, hits[0].Chunk.Origin);
    }

    [Fact]
    public async Task Fingerprints_TrackTheStoredText()
    {
        using var store = CreateStore();
        await store.UpsertAsync([Record("a", "ilk hâli", ChunkOrigin.File, 1f, 0f)]);

        var before = await store.GetFingerprintsAsync();
        Assert.Equal(ChunkFingerprint.Compute("ilk hâli"), before["a"]);

        await store.UpsertAsync([Record("a", "değişmiş hâli", ChunkOrigin.File, 1f, 0f)]);

        var after = await store.GetFingerprintsAsync();
        Assert.Equal(ChunkFingerprint.Compute("değişmiş hâli"), after["a"]);
        Assert.Equal(1, await store.CountAsync());
    }

    [Fact]
    public async Task RemoveChunksNotIn_DropsStaleFileChunksButKeepsNotes()
    {
        using var store = CreateStore();
        await store.UpsertAsync(
        [
            Record("kept", "kalan dosya", ChunkOrigin.File, 1f, 0f),
            Record("stale", "silinen dosya", ChunkOrigin.File, 0f, 1f),
            Record("note", "sohbette öğretilen", ChunkOrigin.Note, 1f, 1f),
        ]);

        var removed = await store.RemoveChunksNotInAsync(["kept"]);

        Assert.Equal(1, removed);
        Assert.Equal(2, await store.CountAsync());

        var ids = (await store.SearchAsync(new[] { 1f, 1f }.AsMemory(), topK: 10))
            .Select(hit => hit.Chunk.Id)
            .ToArray();

        Assert.Contains("kept", ids);
        Assert.Contains("note", ids);          // a note is not produced by any document
        Assert.DoesNotContain("stale", ids);
    }

    [Fact]
    public async Task RemoveChunksNotIn_IsANoOpWhenNothingIsStale()
    {
        using var store = CreateStore();
        await store.UpsertAsync([Record("a", "metin", ChunkOrigin.File, 1f, 0f)]);

        Assert.Equal(0, await store.RemoveChunksNotInAsync(["a"]));
        Assert.Equal(1, await store.CountAsync());
    }

    public void Dispose()
    {
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
