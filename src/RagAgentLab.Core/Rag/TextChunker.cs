using System.Text;

namespace RagAgentLab.Rag;

/// <summary>
/// Splits a document into overlapping, paragraph-aware chunks.
/// <para>
/// Chunking exists because of two hard limits: an embedding model turns a whole chunk into
/// a single vector (so an oversized chunk blurs several topics into one point and retrieval
/// gets vague), and the prompt has a finite context window. The overlap keeps a sentence
/// that happens to fall on a chunk boundary retrievable from both sides.
/// </para>
/// <para>
/// Splitting happens on paragraph boundaries first and only falls back to a hard character
/// cut for paragraphs that are longer than the target size on their own, so chunks stay
/// semantically coherent. The chunk size is therefore a target rather than a
/// hard cap: a coherent paragraph that overshoots it slightly is preferred over a paragraph
/// cut in half. For the same reason a chunk is never closed while it is still shorter than
/// the overlap, which would otherwise produce near-empty chunks out of document headings.
/// </para>
/// </summary>
public static class TextChunker
{
    private static readonly string[] ParagraphSeparators = ["\r\n\r\n", "\n\n"];

    /// <summary>Splits <paramref name="text"/> into chunks.</summary>
    /// <param name="text">Full document text.</param>
    /// <param name="chunkSize">Target maximum chunk length in characters.</param>
    /// <param name="chunkOverlap">How many characters of the previous chunk are repeated at the start of the next one.</param>
    /// <returns>Non-empty chunks in document order.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The sizes are not usable.</exception>
    public static IReadOnlyList<string> Split(string text, int chunkSize, int chunkOverlap)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkSize, 50);
        ArgumentOutOfRangeException.ThrowIfNegative(chunkOverlap);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunkOverlap, chunkSize);

        var chunks = new List<string>();
        var current = new StringBuilder();

        // A chunk is only worth closing once it carries more than the overlap itself;
        // otherwise a short heading paragraph would become a chunk of its own and would
        // then be repeated in full at the start of the next chunk.
        var minimumChunkLength = Math.Max(chunkOverlap, 1);

        foreach (var paragraph in SplitIntoParagraphs(text, chunkSize))
        {
            // Adding this paragraph would overflow the target size: close the current chunk first.
            if (current.Length > minimumChunkLength && current.Length + paragraph.Length + 2 > chunkSize)
            {
                var finished = current.ToString().Trim();
                chunks.Add(finished);

                current.Clear();
                current.Append(TakeOverlap(finished, chunkOverlap));
            }

            if (current.Length > 0)
            {
                current.Append("\n\n");
            }

            current.Append(paragraph);
        }

        if (current.Length > 0)
        {
            var last = current.ToString().Trim();
            if (last.Length > 0)
            {
                chunks.Add(last);
            }
        }

        return chunks;
    }

    /// <summary>
    /// Yields paragraphs, hard-splitting any paragraph that is longer than the target chunk size.
    /// </summary>
    private static IEnumerable<string> SplitIntoParagraphs(string text, int chunkSize)
    {
        var paragraphs = text
            .Replace("\r\n", "\n")
            .Split(ParagraphSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length <= chunkSize)
            {
                yield return paragraph;
                continue;
            }

            for (var offset = 0; offset < paragraph.Length; offset += chunkSize)
            {
                yield return paragraph.Substring(offset, Math.Min(chunkSize, paragraph.Length - offset)).Trim();
            }
        }
    }

    /// <summary>
    /// Returns the trailing <paramref name="overlap"/> characters of a chunk, snapped forward to a
    /// line boundary when there is one and to a word boundary otherwise.
    /// <para>
    /// Preferring the line boundary matters for anything written one record per line. A table of
    /// "2014 yılı ikinci yarı asgari ücret: net 891,03 TL" rows, cut mid-line, leaves the next
    /// chunk starting at "yılı ikinci yarı ... net 891,03 TL" — an amount with no year attached,
    /// directly above the 2015 rows. A model reading that chunk answered a question about 2015
    /// with 2014's figure, and was not wrong to: the year had been chopped off by the chunker.
    /// Snapping to the newline keeps every line whole.
    /// </para>
    /// </summary>
    private static string TakeOverlap(string chunk, int overlap)
    {
        if (overlap == 0 || chunk.Length <= overlap)
        {
            return chunk.Length <= overlap ? chunk : string.Empty;
        }

        var tail = chunk[^overlap..];

        var firstNewline = tail.IndexOf('\n');
        if (firstNewline >= 0)
        {
            return tail[(firstNewline + 1)..];
        }

        var firstSpace = tail.IndexOf(' ');
        return firstSpace >= 0 ? tail[(firstSpace + 1)..] : tail;
    }
}
