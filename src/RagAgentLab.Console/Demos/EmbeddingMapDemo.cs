using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;
using RagAgentLab.Embeddings;
using RagAgentLab.Infrastructure;
using RagAgentLab.Rag;

namespace RagAgentLab.Demos;

/// <summary>
/// Draws the corpus as a two-dimensional map and saves it as an SVG.
/// <para>
/// "768-dimensional space" is a phrase that explains nothing on its own. This flattens the real
/// vectors of the real corpus onto their two principal components, colours each point by the
/// document it came from, and draws a line from each sample question to the chunks it actually
/// retrieves. What it shows is the thing retrieval depends on: whether documents form separate
/// clusters, and whether a question lands in the right one.
/// </para>
/// </summary>
public sealed class EmbeddingMapDemo
{
    private static readonly string[] Questions =
    [
        "Yurt dışından yılda kaç iş günü çalışabilirim?",
        "Yıllık izin hakkım kaç gün?",
        "Yemek harcırahı ne kadar?",
        "Parolam kaç karakter olmalı?",
    ];

    private static readonly string[] Palette =
        ["#2f9bdc", "#e0a32e", "#4fb477", "#c0607a", "#8a6fd1", "#4aa8a0"];

    private readonly IEmbeddingService _embeddingService;
    private readonly RagOptions _options;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public EmbeddingMapDemo(IEmbeddingService embeddingService, IOptions<RagOptions> options)
    {
        _embeddingService = embeddingService;
        _options = options.Value;
    }

    /// <summary>Builds the map and writes it to the given path.</summary>
    /// <param name="outputPath">Where to save the SVG.</param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    public async Task RunAsync(string outputPath, CancellationToken cancellationToken = default)
    {
        ConsoleUi.Section("Embedding map");

        // Chunk the corpus the same way ingestion does, so the map shows what retrieval sees.
        var directory = Path.Combine(AppContext.BaseDirectory, _options.DataDirectory);
        var chunks = new List<DocumentChunk>();

        foreach (var file in Directory.GetFiles(directory, "*.txt").Order())
        {
            var text = await File.ReadAllTextAsync(file, cancellationToken);
            var fileName = Path.GetFileName(file);
            var title = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? fileName;

            var pieces = TextChunker.Split(text, _options.ChunkSize, _options.ChunkOverlap);
            chunks.AddRange(pieces.Select((piece, index) =>
                new DocumentChunk($"{fileName}#{index}", fileName, title, index, piece)));
        }

        ConsoleUi.Info($"Embedding {chunks.Count} chunk(s)…");
        var chunkVectors = await _embeddingService.EmbedDocumentsAsync(
            chunks.Select(c => c.ToContextualText()).ToArray(), cancellationToken);

        var questionVectors = new List<ReadOnlyMemory<float>>();
        foreach (var question in Questions)
        {
            questionVectors.Add(await _embeddingService.EmbedQueryAsync(question, cancellationToken));
        }

        // Axes are chosen from the corpus alone, then the questions are measured against them,
        // so the map spreads the documents out instead of merely separating questions from them.
        var projection = PrincipalComponentProjector.Fit(chunkVectors);
        var points = chunkVectors.Concat(questionVectors)
            .Select(projection.Transform)
            .ToArray();

        // Retrieval is computed in the full 768 dimensions, not on the flattened map - the lines
        // drawn have to show what actually happens, not what the picture suggests.
        var retrievals = questionVectors
            .Select(query => chunkVectors
                .Select((vector, index) => (Index: index, Score: VectorMath.CosineSimilarity(query, vector)))
                .OrderByDescending(hit => hit.Score)
                .Take(_options.TopK)
                .ToArray())
            .ToArray();

        var svg = Render(chunks, points, retrievals);
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, svg, Encoding.UTF8, cancellationToken);

        ConsoleUi.Success($"Written: {fullPath}");
    }

    private string Render(
        IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<(double X, double Y)> points,
        IReadOnlyList<(int Index, double Score)[]> retrievals)
    {
        const int width = 960;
        const int height = 660;
        const int padding = 70;

        var minX = points.Min(p => p.X);
        var maxX = points.Max(p => p.X);
        var minY = points.Min(p => p.Y);
        var maxY = points.Max(p => p.Y);

        double ScaleX(double x) => padding + (x - minX) / (maxX - minX) * (width - 2 * padding);
        double ScaleY(double y) => height - padding - 40 - (y - minY) / (maxY - minY) * (height - 2 * padding - 110);

        var sources = chunks.Select(c => c.SourceName).Distinct().Order().ToArray();
        var colourOf = sources
            .Select((source, index) => (source, colour: Palette[index % Palette.Length]))
            .ToDictionary(pair => pair.source, pair => pair.colour, StringComparer.Ordinal);

        var svg = new StringBuilder();
        svg.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} {height}" width="{width}" height="{height}" font-family="system-ui, -apple-system, Segoe UI, sans-serif">""");
        svg.AppendLine("""<rect width="100%" height="100%" fill="#15161a"/>""");
        svg.AppendLine($"""<text x="{padding}" y="34" fill="#eceef2" font-size="16" font-weight="600">A map of the knowledge base</text>""");
        svg.AppendLine($"""<text x="{padding}" y="54" fill="#9a9ca6" font-size="12">768-dimensional embedding space flattened onto its two principal axes. Dashed lines: the {_options.TopK} chunks each question actually retrieves, scored in the full space.</text>""");

        // Lines first, so the points sit on top of them.
        for (var q = 0; q < retrievals.Count; q++)
        {
            var questionPoint = points[chunks.Count + q];
            foreach (var (index, score) in retrievals[q])
            {
                var chunkPoint = points[index];
                svg.AppendLine($"""<line x1="{F(ScaleX(questionPoint.X))}" y1="{F(ScaleY(questionPoint.Y))}" x2="{F(ScaleX(chunkPoint.X))}" y2="{F(ScaleY(chunkPoint.Y))}" stroke="#6b6b73" stroke-width="1" stroke-dasharray="3 3" opacity="0.65"/>""");

                var midX = (ScaleX(questionPoint.X) + ScaleX(chunkPoint.X)) / 2;
                var midY = (ScaleY(questionPoint.Y) + ScaleY(chunkPoint.Y)) / 2;
                svg.AppendLine($"""<text x="{F(midX)}" y="{F(midY)}" fill="#6b6b73" font-size="9" font-family="ui-monospace, monospace">{score.ToString("F3", CultureInfo.InvariantCulture)}</text>""");
            }
        }

        // Chunks.
        for (var i = 0; i < chunks.Count; i++)
        {
            var (x, y) = points[i];
            svg.AppendLine($"""<circle cx="{F(ScaleX(x))}" cy="{F(ScaleY(y))}" r="6" fill="{colourOf[chunks[i].SourceName]}" opacity="0.85"/>""");
        }

        // Questions, drawn as diamonds so they cannot be mistaken for corpus points.
        for (var q = 0; q < Questions.Length; q++)
        {
            var (x, y) = points[chunks.Count + q];
            var px = ScaleX(x);
            var py = ScaleY(y);
            svg.AppendLine($"""<path d="M {F(px)} {F(py - 9)} L {F(px + 9)} {F(py)} L {F(px)} {F(py + 9)} L {F(px - 9)} {F(py)} Z" fill="#eceef2" stroke="#15161a" stroke-width="1.5"/>""");

            // Labels flip to the other side near the right edge so they never run off the canvas.
            var labelOnLeft = px > width * 0.6;
            var labelX = labelOnLeft ? px - 13 : px + 13;
            var anchor = labelOnLeft ? "end" : "start";
            svg.AppendLine($"""<text x="{F(labelX)}" y="{F(py + 4)}" text-anchor="{anchor}" fill="#eceef2" font-size="11">{Escape(Questions[q])}</text>""");
        }

        // Legend.
        double legendY = height - 42;
        double legendX = padding;

        foreach (var source in sources)
        {
            var entryWidth = 26 + source.Length * 5.9;
            if (legendX + entryWidth > width - padding)
            {
                legendX = padding;
                legendY += 18;
            }

            svg.AppendLine($"""<circle cx="{F(legendX)}" cy="{F(legendY - 4)}" r="5" fill="{colourOf[source]}"/>""");
            svg.AppendLine($"""<text x="{F(legendX + 11)}" y="{F(legendY)}" fill="#9a9ca6" font-size="10.5">{Escape(source)}</text>""");
            legendX += entryWidth;
        }

        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private static string F(double value) => value.ToString("F1", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
