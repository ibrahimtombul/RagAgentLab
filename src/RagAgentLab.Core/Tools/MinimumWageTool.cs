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
    [Description("Returns the statutory monthly minimum wage in Turkey, both gross (brüt) and " +
                 "net. Use this whenever the question is about the minimum wage (asgari ücret). " +
                 "Leave the year empty for the wage in force right now - the tool knows today's " +
                 "date, so do not look it up or guess it.")]
    public string GetMinimumWage(
        [Description("Calendar year, for example 2015. Leave empty for the current year.")]
        int? year = null,
        [Description("Optional. 'ilk' for the first half of the year or 'ikinci' for the second, " +
                     "for years where the wage changed mid-year. Leave empty to get every period.")]
        string? half = null)
    {
        var entries = _entries.Value;

        var minYear = entries.Min(e => e.Year);
        var maxYear = entries.Max(e => e.Year);

        // "Güncel asgari ücret" is the most common way to ask this, and a language model has no
        // idea what today's date is - left to guess, it reached for a year from its training
        // data. The tool runs on a machine with a clock, so the current year is resolved here
        // rather than being something the model has to chain another tool call to discover.
        var requestedYear = year ?? Math.Min(DateTime.Today.Year, maxYear);

        var matches = entries.Where(entry => entry.Year == requestedYear).ToArray();
        if (matches.Length == 0)
        {
            return $"ERROR: {requestedYear} yılı için kayıt yok. Kapsanan aralık: {minYear}-{maxYear}.";
        }

        if (!string.IsNullOrWhiteSpace(half))
        {
            var filtered = matches
                .Where(entry => string.Equals(entry.Half, half.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (filtered.Length == 0)
            {
                return matches.Length == 1
                    ? $"{requestedYear} yılında asgari ücret yıl içinde değişmedi. " + Describe(matches[0])
                    : $"ERROR: '{half}' geçersiz. 'ilk' veya 'ikinci' kullanın.";
            }

            matches = filtered;
        }

        return string.Join(" ", matches.Select(Describe));
    }

    /// <summary>Expresses a salary as a multiple of the minimum wage.</summary>
    /// <param name="salary">The salary to compare, in Turkish lira.</param>
    /// <param name="basis">Whether to compare against the net or the gross minimum wage.</param>
    /// <param name="year">Optional year; defaults to the one in force now.</param>
    /// <returns>The comparison, or a human-readable error.</returns>
    [KernelFunction("compare_salary_to_minimum_wage")]
    [Description("Compares a salary to the statutory minimum wage and returns the ratio. " +
                 "Use this whenever someone asks how their salary compares to the minimum wage, " +
                 "or how many times the minimum wage they earn. Compares against the NET minimum " +
                 "wage by default, which is what such comparisons normally mean. Do the comparison " +
                 "with this tool rather than working the ratio out yourself.")]
    public string CompareSalaryToMinimumWage(
        [Description("The salary to compare, in Turkish lira, for example 64000.")]
        double salary,
        [Description("Optional. 'net' (the default) or 'brut'.")]
        string basis = "net",
        [Description("Optional calendar year. Leave empty for the wage in force right now.")]
        int? year = null)
    {
        if (salary <= 0)
        {
            return "ERROR: Maaş sıfırdan büyük olmalı.";
        }

        var entries = _entries.Value;
        var maxYear = entries.Max(e => e.Year);
        var requestedYear = year ?? Math.Min(DateTime.Today.Year, maxYear);

        // The last period of a year is the one still in force at the end of it, which is what
        // "the minimum wage for <year>" means once the year is over.
        var entry = entries.LastOrDefault(e => e.Year == requestedYear);
        if (entry is null)
        {
            return $"ERROR: {requestedYear} yılı için kayıt yok. " +
                   $"Kapsanan aralık: {entries.Min(e => e.Year)}-{maxYear}.";
        }

        var useGross = basis.Trim().StartsWith("br", StringComparison.OrdinalIgnoreCase);
        var reference = useGross ? entry.Gross : entry.Net;
        var label = useGross ? "brüt" : "net";
        var ratio = (decimal)salary / reference;

        return $"{Format((decimal)salary)} TL, {requestedYear} yılı {label} asgari ücretinin " +
               $"({Format(reference)} TL) {ratio.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"))} katıdır.";
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

        return $"{period} aylık asgari ücret net {Format(entry.Net)} TL, brüt {Format(entry.Gross)} TL.";
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
