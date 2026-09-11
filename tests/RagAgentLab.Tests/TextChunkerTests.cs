using RagAgentLab.Rag;

namespace RagAgentLab.Tests;

/// <summary>
/// Tests for the chunker. Chunking is the part of a RAG system that fails quietly — bad
/// chunks do not crash anything, they just make retrieval worse — so the invariants are
/// worth asserting explicitly.
/// </summary>
public sealed class TextChunkerTests
{
    private const string Document =
        """
        KUZEY YAZILIM A.Ş. - İZİN POLİTİKASI

        1. YILLIK ÜCRETLİ İZİN
        Çalışanlar deneme süresini tamamladıktan sonra yıllık ücretli izin hakkı kazanır.
        Kıdem yılına göre izin günleri değişir.

        2. İZİN TALEP SÜRECİ
        Talepler en az 10 iş günü önce İK portalı üzerinden oluşturulur.

        3. HASTALIK İZNİ
        Rapor sunmaksızın yılda 3 güne kadar hastalık izni kullanılabilir.
        """;

    [Fact]
    public void Split_ProducesNonEmptyTrimmedChunks()
    {
        var chunks = TextChunker.Split(Document, chunkSize: 200, chunkOverlap: 40);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.False(string.IsNullOrWhiteSpace(chunk));
            Assert.Equal(chunk.Trim(), chunk);
        });
    }

    [Fact]
    public void Split_KeepsEveryChunkLongerThanTheOverlap()
    {
        // Regression test: a short heading paragraph used to be emitted as a chunk of its
        // own and was then repeated in full at the start of the next chunk.
        const int overlap = 60;
        var chunks = TextChunker.Split(Document, chunkSize: 220, chunkOverlap: overlap);

        Assert.All(chunks, chunk => Assert.True(
            chunk.Length > overlap,
            $"Chunk of {chunk.Length} chars is not longer than the {overlap}-char overlap: '{chunk}'"));
    }

    [Fact]
    public void Split_PreservesAllContent()
    {
        var chunks = TextChunker.Split(Document, chunkSize: 200, chunkOverlap: 40);

        // Every sentence of the source must survive somewhere in the output.
        Assert.Contains(chunks, c => c.Contains("yıllık ücretli izin hakkı kazanır"));
        Assert.Contains(chunks, c => c.Contains("en az 10 iş günü önce"));
        Assert.Contains(chunks, c => c.Contains("3 güne kadar hastalık izni"));
    }

    [Fact]
    public void Split_OverlapsConsecutiveChunks()
    {
        var chunks = TextChunker.Split(Document, chunkSize: 150, chunkOverlap: 50);

        Assert.True(chunks.Count > 1, "The document should not fit into a single chunk.");

        // The tail of one chunk must reappear at the head of the next, so a sentence sitting
        // on a boundary stays retrievable from both sides.
        var overlapFound = chunks
            .Zip(chunks.Skip(1), (first, second) => (first, second))
            .Any(pair => pair.second.Split(' ')[0] is var firstWord &&
                         pair.first.Contains(firstWord, StringComparison.Ordinal));

        Assert.True(overlapFound, "No overlap was found between consecutive chunks.");
    }

    [Fact]
    public void Split_HardSplitsAParagraphLongerThanTheChunkSize()
    {
        var longParagraph = new string('a', 500);

        var chunks = TextChunker.Split(longParagraph, chunkSize: 100, chunkOverlap: 20);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= 220));
    }

    [Theory]
    [InlineData(10, 5)]      // chunk size below the supported minimum
    [InlineData(200, 200)]   // overlap equal to the chunk size
    [InlineData(200, 300)]   // overlap larger than the chunk size
    [InlineData(200, -1)]    // negative overlap
    public void Split_RejectsInvalidSizes(int chunkSize, int chunkOverlap) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Split(Document, chunkSize, chunkOverlap));
}
