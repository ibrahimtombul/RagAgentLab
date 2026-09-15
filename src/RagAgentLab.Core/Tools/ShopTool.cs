using System.ComponentModel;
using System.Globalization;
using Microsoft.SemanticKernel;
using RagAgentLab.Shop;

namespace RagAgentLab.Tools;

/// <summary>
/// Exposes the operational database to the agent.
/// <para>
/// This is the other half of the argument the policy corpus makes. An HR policy is prose: the
/// answer is a sentence somewhere in a document, and finding it approximately is exactly right.
/// A stock level is not prose. It changes daily, it has to be exact, and questions about it are
/// often aggregations — "the five best sellers of the last month" is not a passage that exists
/// anywhere to be found. Embedding rows of a table and hoping similarity picks the right one is
/// the mistake this project already measured with a wage table; here the database answers instead.
/// </para>
/// <para>
/// The model chooses the question and its arguments; it never writes SQL. Every query behind
/// these functions is hand-written and parameterised, so a misunderstood question produces a
/// wrong answer rather than an arbitrary query against the schema.
/// </para>
/// </summary>
public sealed class ShopTool
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly ShopQueries _queries;

    /// <summary>Creates a new instance. Resolved from DI by Semantic Kernel.</summary>
    public ShopTool(ShopQueries queries) => _queries = queries;

    /// <summary>Returns current stock for one product.</summary>
    /// <param name="product">Product code or part of the product name.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    [KernelFunction("get_stock")]
    [Description("Returns the current stock of a product, broken down by warehouse. " +
                 "Use this for any question about how many units of something there are, " +
                 "or where stock is held. Accepts a product code such as ELK-001 or part of the name.")]
    public async Task<string> GetStockAsync(
        [Description("Product code, for example 'ELK-001', or part of the name, for example 'kulaklık'.")]
        string product,
        CancellationToken cancellationToken = default)
    {
        var stock = await _queries.GetStockAsync(product, cancellationToken);

        return stock is null
            ? $"ERROR: '{product}' ile eşleşen ürün yok."
            : ShopQueries.Describe(stock);
    }

    /// <summary>Returns the best-selling products over a date range.</summary>
    /// <param name="from">First day, yyyy-MM-dd.</param>
    /// <param name="to">Last day, yyyy-MM-dd.</param>
    /// <param name="limit">How many products to list.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    [KernelFunction("top_selling_products")]
    [Description("Returns the best-selling products over a period, by units sold. " +
                 "Use this for questions about what sold most, or best sellers. " +
                 "Leave both dates empty for the last 30 days — the tool knows today's date, " +
                 "so do not look it up or guess it.")]
    public async Task<string> GetTopSellingAsync(
        [Description("First day, in yyyy-MM-dd format. Leave empty for 30 days ago.")]
        string? from = null,
        [Description("Last day, in yyyy-MM-dd format. Leave empty for today.")]
        string? to = null,
        [Description("How many products to list. Defaults to 5.")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRange(from, to, out var start, out var end, out var error))
        {
            return error;
        }

        var rows = await _queries.GetBestSellersAsync(start, end, Math.Clamp(limit, 1, 20), cancellationToken);
        if (rows.Count == 0)
        {
            return $"{start:yyyy-MM-dd} - {end:yyyy-MM-dd} aralığında satış kaydı yok.";
        }

        var lines = rows.Select((row, index) =>
            $"{index + 1}. {row.ProductName} ({row.ProductCode}): {row.UnitsSold} adet, " +
            $"{ShopQueries.FormatMoney(row.Revenue)} TL");

        return $"{start:yyyy-MM-dd} - {end:yyyy-MM-dd} aralığında en çok satanlar: " + string.Join(" | ", lines);
    }

    /// <summary>Returns total units and revenue over a date range.</summary>
    /// <param name="from">First day, yyyy-MM-dd.</param>
    /// <param name="to">Last day, yyyy-MM-dd.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    [KernelFunction("sales_total")]
    [Description("Returns the total units sold and the total revenue over a period. " +
                 "Use this for questions about turnover, revenue or how much was sold. " +
                 "Leave both dates empty for the last 30 days — the tool knows today's date, " +
                 "so do not look it up or guess it.")]
    public async Task<string> GetSalesTotalAsync(
        [Description("First day, in yyyy-MM-dd format. Leave empty for 30 days ago.")]
        string? from = null,
        [Description("Last day, in yyyy-MM-dd format. Leave empty for today.")]
        string? to = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseRange(from, to, out var start, out var end, out var error))
        {
            return error;
        }

        var (units, revenue) = await _queries.GetSalesTotalAsync(start, end, cancellationToken);

        return $"{start:yyyy-MM-dd} - {end:yyyy-MM-dd} aralığında toplam {units} adet satıldı, " +
               $"ciro {ShopQueries.FormatMoney(revenue)} TL.";
    }

    /// <summary>
    /// Parses and orders a date range, filling in the last thirty days when it is left open.
    /// <para>
    /// "The last 30 days" is how people ask this, and the model has no idea what today's date is:
    /// left to supply the dates itself it reached for a year out of its training data and the
    /// query came back empty. The same lesson as the wage tool — the context the model lacks goes
    /// into the tool, which runs on a machine with a clock.
    /// </para>
    /// </summary>
    private static bool TryParseRange(
        string? from,
        string? to,
        out DateOnly start,
        out DateOnly end,
        out string error)
    {
        error = string.Empty;
        var today = DateOnly.FromDateTime(DateTime.Today);

        end = today;
        if (!string.IsNullOrWhiteSpace(to) &&
            !DateOnly.TryParseExact(to.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out end))
        {
            error = $"ERROR: '{to}' yyyy-MM-dd biçiminde bir tarih değil.";
            start = default;
            return false;
        }

        start = end.AddDays(-29);
        if (!string.IsNullOrWhiteSpace(from) &&
            !DateOnly.TryParseExact(from.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out start))
        {
            error = $"ERROR: '{from}' yyyy-MM-dd biçiminde bir tarih değil.";
            return false;
        }

        if (start > end)
        {
            (start, end) = (end, start);
        }

        return true;
    }
}
