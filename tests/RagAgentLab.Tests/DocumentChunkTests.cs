using RagAgentLab.Embeddings;

namespace RagAgentLab.Tests;

/// <summary>Tests for the contextual text that is embedded and injected into prompts.</summary>
public sealed class DocumentChunkTests
{
    [Fact]
    public void ToContextualText_PrependsSourceAndTitle()
    {
        var chunk = new DocumentChunk(
            Id: "02-uzaktan-calisma-politikasi.txt#1",
            SourceName: "02-uzaktan-calisma-politikasi.txt",
            DocumentTitle: "KUZEY YAZILIM A.Ş. - UZAKTAN VE HİBRİT ÇALIŞMA POLİTİKASI",
            ChunkIndex: 1,
            Text: "Yeni işe başlayan çalışanlar ilk 8 hafta haftada 4 gün ofiste çalışır.");

        var contextual = chunk.ToContextualText();

        Assert.StartsWith("[02-uzaktan-calisma-politikasi.txt — KUZEY YAZILIM", contextual);
        Assert.Contains(chunk.Text, contextual);
    }
}
