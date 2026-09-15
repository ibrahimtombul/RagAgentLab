using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace RagAgentLab.Shop;

/// <summary>Stock held for one product, broken down by warehouse.</summary>
/// <param name="ProductCode">Product code.</param>
/// <param name="ProductName">Product name.</param>
/// <param name="ByWarehouse">Quantity per warehouse city.</param>
/// <param name="Total">Total across every warehouse.</param>
public sealed record StockLevel(
    string ProductCode,
    string ProductName,
    IReadOnlyList<(string City, int Quantity)> ByWarehouse,
    int Total);

/// <summary>One row of a best-sellers report.</summary>
/// <param name="ProductCode">Product code.</param>
/// <param name="ProductName">Product name.</param>
/// <param name="UnitsSold">Units sold in the period.</param>
/// <param name="Revenue">Revenue in the period.</param>
public sealed record BestSeller(string ProductCode, string ProductName, int UnitsSold, decimal Revenue);

/// <summary>
/// The SQL behind the shop tools, kept free of Semantic Kernel so it can be tested directly
/// against a seeded database — the same split as the calculator's expression parser and the tool
/// that exposes it.
/// <para>
/// Every query is parameterised and hand-written. The model chooses <em>which</em> question to
/// ask and with what arguments; it never writes SQL. That keeps the blast radius of a
/// misunderstood question to a wrong-but-harmless answer, rather than to whatever query a 7B
/// model might compose against a production schema.
/// </para>
/// </summary>
public sealed class ShopQueries
{
    private readonly ShopDatabase _database;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public ShopQueries(ShopDatabase database) => _database = database;

    /// <summary>Finds stock for a product by code, or by a fragment of its name.</summary>
    /// <param name="product">Product code such as <c>ELK-001</c>, or part of the name.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>The stock level, or null when nothing matches.</returns>
    public async Task<StockLevel?> GetStockAsync(string product, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(product);

        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT p.code, p.name, w.city, s.quantity
            FROM products p
            JOIN stock s      ON s.product_code = p.code
            JOIN warehouses w ON w.code = s.warehouse_code
            WHERE p.code = $q COLLATE NOCASE OR p.name LIKE '%' || $q || '%'
            ORDER BY w.city;
            """;
        command.Parameters.AddWithValue("$q", product.Trim());

        string? code = null;
        string? name = null;
        var byWarehouse = new List<(string, int)>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            code ??= reader.GetString(0);
            name ??= reader.GetString(1);

            // A name fragment can match more than one product; the first one wins and the rest
            // are ignored rather than silently summed into a meaningless total.
            if (reader.GetString(0) != code)
            {
                continue;
            }

            byWarehouse.Add((reader.GetString(2), reader.GetInt32(3)));
        }

        return code is null
            ? null
            : new StockLevel(code, name!, byWarehouse, byWarehouse.Sum(x => x.Item2));
    }

    /// <summary>Returns the best-selling products over a date range, by units sold.</summary>
    /// <param name="from">First day included.</param>
    /// <param name="to">Last day included.</param>
    /// <param name="limit">How many rows to return.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    public async Task<IReadOnlyList<BestSeller>> GetBestSellersAsync(
        DateOnly from,
        DateOnly to,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT p.code, p.name, SUM(s.quantity), SUM(s.quantity * s.unit_price)
            FROM sales s
            JOIN products p ON p.code = s.product_code
            WHERE s.sold_on BETWEEN $from AND $to
            GROUP BY p.code, p.name
            ORDER BY SUM(s.quantity) DESC, p.code
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$limit", limit);

        var rows = new List<BestSeller>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new BestSeller(
                reader.GetString(0), reader.GetString(1), reader.GetInt32(2), (decimal)reader.GetDouble(3)));
        }

        return rows;
    }

    /// <summary>Returns total units and revenue over a date range.</summary>
    /// <param name="from">First day included.</param>
    /// <param name="to">Last day included.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    public async Task<(int Units, decimal Revenue)> GetSalesTotalAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _database.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            """
            SELECT COALESCE(SUM(quantity), 0), COALESCE(SUM(quantity * unit_price), 0)
            FROM sales
            WHERE sold_on BETWEEN $from AND $to;
            """;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        return (reader.GetInt32(0), (decimal)reader.GetDouble(1));
    }

    /// <summary>Formats an amount the way the answers quote it.</summary>
    public static string FormatMoney(decimal amount) =>
        amount.ToString("N2", CultureInfo.GetCultureInfo("tr-TR"));

    /// <summary>Renders a stock level as a sentence the model can quote directly.</summary>
    public static string Describe(StockLevel stock)
    {
        var builder = new StringBuilder();
        builder.Append($"{stock.ProductName} ({stock.ProductCode}) toplam {stock.Total} adet. ");
        builder.Append("Depo dağılımı: ");
        builder.Append(string.Join(", ", stock.ByWarehouse.Select(w => $"{w.City} {w.Quantity} adet")));
        builder.Append('.');
        return builder.ToString();
    }
}
