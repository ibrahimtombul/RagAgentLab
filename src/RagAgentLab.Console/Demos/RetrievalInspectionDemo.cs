using RagAgentLab.Embeddings;
using RagAgentLab.Infrastructure;
using RagAgentLab.Rag;
using RagAgentLab.Shop;

namespace RagAgentLab.Demos;

/// <summary>
/// Prints the retrieval scores for one or more questions, without generating an answer.
/// <para>
/// Retrieval quality is the part of a RAG system that decides everything downstream, and it
/// is measurable on its own: a question whose best match scores no better than an unrelated
/// one tells you the corpus does not contain the answer. This mode exists to calibrate the
/// similarity threshold with numbers rather than by guessing at one.
/// </para>
/// </summary>
public sealed class RetrievalInspectionDemo
{
    private readonly KnowledgeBaseIngestor _ingestor;
    private readonly ProductCatalogIndexer _catalogIndexer;
    private readonly IRagPipeline _ragPipeline;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public RetrievalInspectionDemo(
        KnowledgeBaseIngestor ingestor,
        ProductCatalogIndexer catalogIndexer,
        IRagPipeline ragPipeline)
    {
        _ingestor = ingestor;
        _catalogIndexer = catalogIndexer;
        _ragPipeline = ragPipeline;
    }

    /// <summary>Indexes the corpus, then prints the top matches for each question.</summary>
    /// <param name="questions">Questions to score; falls back to a built-in set when empty.</param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    public async Task RunAsync(IReadOnlyList<string> questions, CancellationToken cancellationToken = default)
    {
        var report = await _ingestor.IngestAsync(cancellationToken);
        var products = await _catalogIndexer.IndexAsync(cancellationToken);
        ConsoleUi.Success(
            $"{report.DocumentCount} document(s) -> {report.ChunkCount} chunk(s), " +
            $"{products} product description(s) indexed.");

        foreach (var question in questions)
        {
            var hits = await _ragPipeline.RetrieveAsync(question, cancellationToken: cancellationToken);

            Console.WriteLine();
            ConsoleUi.Step("Question", question);

            foreach (var hit in hits)
            {
                var label = hit.Chunk.Origin == ChunkOrigin.Product
                    ? $"{hit.Chunk.SourceName} — {hit.Chunk.DocumentTitle}"
                    : $"{hit.Chunk.SourceName} (chunk {hit.Chunk.ChunkIndex})";

                ConsoleUi.Info($"  {hit.Score:F3}  [{hit.Chunk.Origin}] {label}");
            }
        }
    }
}
