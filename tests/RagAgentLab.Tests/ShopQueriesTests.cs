using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;
using RagAgentLab.Shop;

namespace RagAgentLab.Tests;

/// <summary>
/// Tests for the operational queries the agent can call. The seed is generated from a formula
/// rather than at random, so these assert exact numbers — which is the whole point of putting
/// this data behind SQL instead of in the vector store.
/// </summary>
public sealed class ShopQueriesTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"ragagentlab-shop-{Guid.NewGuid():N}.db");

    private readonly ShopQueries _queries;

    public ShopQueriesTests()
    {
        var database = new ShopDatabase(
            Options.Create(new ShopOptions { DatabasePath = _databasePath }),
            NullLogger<ShopDatabase>.Instance);

        _queries = new ShopQueries(database);
    }

    [Fact]
    public async Task GetStock_FindsAProductByItsCode()
    {
        var stock = await _queries.GetStockAsync("ELK-001");

        Assert.NotNull(stock);
        Assert.Equal("Kablosuz Kulaklık", stock!.ProductName);
        Assert.Equal(3, stock.ByWarehouse.Count);
        Assert.Equal(stock.ByWarehouse.Sum(w => w.Quantity), stock.Total);
    }

    [Fact]
    public async Task GetStock_FindsAProductByPartOfItsName()
    {
        var stock = await _queries.GetStockAsync("kulaklık");

        Assert.NotNull(stock);
        Assert.Equal("ELK-001", stock!.ProductCode);
    }

    [Fact]
    public async Task GetStock_ReturnsNullForSomethingNotStocked() =>
        Assert.Null(await _queries.GetStockAsync("buzdolabı"));

    [Fact]
    public async Task GetStock_BreaksDownByWarehouse()
    {
        var stock = await _queries.GetStockAsync("EVA-001");

        Assert.NotNull(stock);
        Assert.Equal(["Ankara", "İstanbul", "İzmir"], stock!.ByWarehouse.Select(w => w.City).Order());
        Assert.All(stock.ByWarehouse, w => Assert.True(w.Quantity > 0));
    }

    [Fact]
    public async Task GetBestSellers_RanksByUnitsSoldAndRespectsTheLimit()
    {
        var from = ShopDatabase.SeedEnd.AddDays(-29);

        var rows = await _queries.GetBestSellersAsync(from, ShopDatabase.SeedEnd, limit: 3);

        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].UnitsSold >= rows[1].UnitsSold);
        Assert.True(rows[1].UnitsSold >= rows[2].UnitsSold);
        Assert.All(rows, row => Assert.True(row.Revenue > 0));
    }

    [Fact]
    public async Task GetBestSellers_ReturnsNothingForAPeriodWithNoSales()
    {
        var future = ShopDatabase.SeedEnd.AddDays(30);

        Assert.Empty(await _queries.GetBestSellersAsync(future, future.AddDays(10)));
    }

    [Fact]
    public async Task GetSalesTotal_AgreesWithTheSumOfEveryProduct()
    {
        // The aggregate has to match the parts: this is the kind of question similarity search
        // cannot answer at all, and the kind SQL answers exactly.
        var from = ShopDatabase.SeedEnd.AddDays(-29);

        var (units, revenue) = await _queries.GetSalesTotalAsync(from, ShopDatabase.SeedEnd);
        var perProduct = await _queries.GetBestSellersAsync(from, ShopDatabase.SeedEnd, limit: 20);

        Assert.Equal(perProduct.Sum(row => row.UnitsSold), units);
        Assert.Equal(perProduct.Sum(row => row.Revenue), revenue);
    }

    [Fact]
    public async Task GetSalesTotal_IsZeroForAPeriodWithNoSales()
    {
        var future = ShopDatabase.SeedEnd.AddDays(30);

        var (units, revenue) = await _queries.GetSalesTotalAsync(future, future.AddDays(10));

        Assert.Equal(0, units);
        Assert.Equal(0m, revenue);
    }

    [Fact]
    public async Task Seeding_IsStableAcrossInstances()
    {
        // A second instance over the same file must not re-seed and double the numbers.
        var again = new ShopQueries(new ShopDatabase(
            Options.Create(new ShopOptions { DatabasePath = _databasePath }),
            NullLogger<ShopDatabase>.Instance));

        var first = await _queries.GetStockAsync("ELK-001");
        var second = await again.GetStockAsync("ELK-001");

        Assert.Equal(first!.Total, second!.Total);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }
}
