using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel;

namespace RagAgentLab.Tools;

/// <summary>
/// Looks up the statutory monthly minimum wage for a given year.
/// <para>
/// This tool exists because of a measurement, not a preference. The same figures were first
/// added to the corpus as a text document, one record per line, and retrieved through the
/// normal RAG path. Retrieval found the right document every time — but every row embeds to
/// almost the same vector, because "2014 yılı asgari ücret" and "2015 yılı asgari ücret"
/// differ by one token that carries no semantic weight, and the model then had to pick the
/// right row out of three overlapping chunks. Asked about 2015 it answered with 2014's
/// figure.
/// </para>
/// <para>
/// A year-to-amount table is a lookup, and a lookup should be exact. Embedding similarity is
/// the wrong instrument for it in the same way it is the wrong instrument for arithmetic,
/// which is why this sits beside the calculator rather than in the vector store.
/// </para>
/// </summary>
public sealed class MinimumWageTool
{
    private const string DataFileName = "reference-data/minimum-wage.json";

    private readonly Lazy<IReadOnlyList<MinimumWageEntry>> _entries = new(LoadEntries);

    /// <summary>Returns the minimum wage for a year, both gross and net.</summary>
    /// <param name="year">Calendar year to look up.</param>
    /// <param name="half">Optional half of the year for years that changed mid-year.</param>
    /// <returns>The figures, or a human-readable error the model can act on.</returns>
    [KernelFunction("get_minimum_wage")]
    [Description("Returns the statutory monthly minimum wage in Turkey for a given year, both " +
                 "gross (brüt) and net. Use this whenever the question is about the minimum wage " +
                 "(asgari ücret) of a specific year.")]
    public string GetMinimumWage(
        [Description("Calendar year, for example 2015.")] int year,
        [Description("Optional. 'ilk' for the first half of the year or 'ikinci' for the second, " +
                     "for years where the wage changed mid-year. Leave empty to get every period.")]
        string? half = null)
    {
        var entries = _entries.Value;

        var minYear = entries.Min(e => e.Year);
        var maxYear = entries.Max(e => e.Year);

        var matches = entries.Where(entry => entry.Year == year).ToArray();
        if (matches.Length == 0)
        {
            return $"ERROR: {year} yılı için kayıt yok. Kapsanan aralık: {minYear}-{maxYear}.";
        }

        if (!string.IsNullOrWhiteSpace(half))
        {
            var filtered = matches
                .Where(entry => string.Equals(entry.Half, half.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (filtered.Length == 0)
            {
                return matches.Length == 1
                    ? $"{year} yılında asgari ücret yıl içinde değişmedi. " + Describe(matches[0])
                    : $"ERROR: '{half}' geçersiz. 'ilk' veya 'ikinci' kullanın.";
            }

            matches = filtered;
        }

        return string.Join(" ", matches.Select(Describe));
    }

    /// <summary>Renders one entry as a sentence the model can quote directly.</summary>
    private static string Describe(MinimumWageEntry entry)
    {
        var period = entry.Half switch
        {
            "ilk" => $"{entry.Year} yılı ilk yarısında",
            "ikinci" => $"{entry.Year} yılı ikinci yarısında",
            _ => $"{entry.Year} yılında",
        };

        return $"{period} aylık asgari ücret brüt {Format(entry.Gross)} TL, net {Format(entry.Net)} TL.";
    }

    private static string Format(decimal amount) =>
        amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));

    /// <summary>
    /// Reads the table once, on first use. It is a static file rather than a dictionary in code
    /// so that correcting a figure or appending next year's is an edit to data, not a rebuild
    /// of the application.
    /// </summary>
    private static IReadOnlyList<MinimumWageEntry> LoadEntries()
    {
        var path = Path.Combine(AppContext.BaseDirectory, DataFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Minimum wage reference data not found: {path}", path);
        }

        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<MinimumWageTable>(stream)
                       ?? throw new InvalidOperationException($"Could not read {DataFileName}.");

        return document.Entries;
    }

    private sealed record MinimumWageTable
    {
        [JsonPropertyName("entries")]
        public List<MinimumWageEntry> Entries { get; init; } = [];
    }

    private sealed record MinimumWageEntry
    {
        [JsonPropertyName("year")]
        public int Year { get; init; }

        /// <summary>"ilk", "ikinci", or null when the wage held for the whole year.</summary>
        [JsonPropertyName("half")]
        public string? Half { get; init; }

        [JsonPropertyName("gross")]
        public decimal Gross { get; init; }

        [JsonPropertyName("net")]
        public decimal Net { get; init; }
    }
}
