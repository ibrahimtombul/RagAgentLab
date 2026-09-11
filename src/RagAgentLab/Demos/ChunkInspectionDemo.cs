using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;
using RagAgentLab.Infrastructure;
using RagAgentLab.Rag;

namespace RagAgentLab.Demos;

/// <summary>
/// Prints how each source document is split into chunks, without calling the model server.
/// <para>
/// Chunking is the part of a RAG system that is easiest to get quietly wrong — chunks that
/// are too large blur several topics into one vector, chunks that are too small lose the
/// context a sentence needs — and it is pure, offline logic. Having it inspectable on its
/// own means the chunk size can be tuned (and demonstrated) without an LLM running.
/// </para>
/// </summary>
public sealed class ChunkInspectionDemo
{
    private readonly RagOptions _options;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public ChunkInspectionDemo(IOptions<RagOptions> options) => _options = options.Value;

    /// <summary>Chunks every document in the data directory and prints a preview of each chunk.</summary>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ConsoleUi.Section($"Chunking preview (size {_options.ChunkSize}, overlap {_options.ChunkOverlap})");

        var directory = Path.Combine(AppContext.BaseDirectory, _options.DataDirectory);
        var files = Directory.GetFiles(directory, "*.txt").Order().ToArray();
        var total = 0;

        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var chunks = TextChunker.Split(text, _options.ChunkSize, _options.ChunkOverlap);
            total += chunks.Count;

            Console.WriteLine();
            ConsoleUi.Step(Path.GetFileName(file), $"{text.Length} chars -> {chunks.Count} chunk(s)");

            foreach (var (chunk, index) in chunks.Select((c, i) => (c, i)))
            {
                var preview = chunk.ReplaceLineEndings(" ");
                if (preview.Length > 90)
                {
                    preview = preview[..90] + "...";
                }

                ConsoleUi.Info($"  [{index}] {chunk.Length,4} chars | {preview}");
            }
        }

        Console.WriteLine();
        ConsoleUi.Success($"{files.Length} document(s) -> {total} chunk(s) total.");
    }
}
