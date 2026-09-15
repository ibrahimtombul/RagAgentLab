using RagAgentLab.Embeddings;
using RagAgentLab.Infrastructure;

namespace RagAgentLab.Demos;

/// <summary>
/// Measures what the embedding model actually considers similar, in Turkish and in English.
/// <para>
/// Everything retrieval does rests on one assumption: that texts meaning the same thing land
/// near each other in the vector space. That assumption is testable in a few seconds, and for
/// this corpus's language it does not entirely hold — which is the root cause of a retrieval
/// finding recorded elsewhere in this project, that no fixed similarity threshold separates
/// in-scope questions from out-of-scope ones.
/// </para>
/// </summary>
public sealed class SimilarityProbeDemo
{
    /// <summary>A reference question and three others at decreasing distance from it.</summary>
    private sealed record ProbeSet(string Language, string Reference, (string Label, string Text)[] Others);

    private static readonly ProbeSet[] Probes =
    [
        new("Türkçe", "Yıllık izin hakkım kaç gün?",
        [
            ("aynı anlam, farklı kelimeler", "Senelik tatil süresi ne kadar?"),
            ("farklı konu, aynı alan", "Yemek harcırahı ne kadar?"),
            ("tamamen alakasız", "Python'da liste nasıl sıralanır?"),
        ]),
        new("English", "How many days of annual leave do I get?",
        [
            ("same meaning, different words", "What is my yearly vacation entitlement?"),
            ("different topic, same domain", "How much is the daily meal allowance?"),
            ("unrelated", "How do I sort a list in Python?"),
        ]),
    ];

    private readonly IEmbeddingService _embeddingService;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public SimilarityProbeDemo(IEmbeddingService embeddingService) => _embeddingService = embeddingService;

    /// <summary>Runs every probe set and prints the similarities.</summary>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ConsoleUi.Section("Embedding similarity probe");
        ConsoleUi.Info("1,000 = aynı yön · 0,000 = ilgisiz");

        foreach (var probe in Probes)
        {
            Console.WriteLine();
            ConsoleUi.Step(probe.Language, probe.Reference);

            var reference = await _embeddingService.EmbedQueryAsync(probe.Reference, cancellationToken);

            foreach (var (label, text) in probe.Others)
            {
                var other = await _embeddingService.EmbedQueryAsync(text, cancellationToken);
                var score = VectorMath.CosineSimilarity(reference, other);

                ConsoleUi.Info($"  {score:F3}  {label,-30} \"{text}\"");
            }
        }

        Console.WriteLine();
        ConsoleUi.Info("Beklenen sıralama: aynı anlam > farklı konu > alakasız.");
    }
}
