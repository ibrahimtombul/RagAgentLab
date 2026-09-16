using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;

namespace RagAgentLab.Shop;

/// <summary>
/// Creates and seeds the sample e-commerce database.
/// <para>
/// It stands in for the operational database a real assistant would sit next to: products, a few
/// warehouses, stock per warehouse and a few months of sales. The point of having it in this
/// project is the contrast — the HR policies are prose and belong in the vector store, while none
/// of this does. A stock level is a fact to be looked up exactly, not a passage to be found
/// approximately.
/// </para>
/// <para>
/// The data is generated from a formula rather than at random, so every run and every test sees
/// the same numbers.
/// </para>
/// <para>
/// One column is the exception to all of that. A product's description is prose, and prose is
/// what the vector store is for — so the same row is served two ways: its numbers through SQL,
/// its description through retrieval. The split is per question, not per table.
/// </para>
/// </summary>
public sealed class ShopDatabase
{
    private static readonly (string Code, string Name, string Category, decimal Price, string Description)[] Products =
    [
        ("ELK-001", "Kablosuz Kulaklık", "Elektronik", 2499m,
            "Uzun yolculuklarda ve kalabalık ofiste çevredeki gürültüyü bastırır. Tek şarjla gün boyu " +
            "dayanır, yumuşak yastıkları saatlerce takıldığında bile kulağı acıtmaz."),
        ("ELK-002", "Bluetooth Hoparlör", "Elektronik", 1299m,
            "Bahçede, piknikte ve banyoda gönül rahatlığıyla kullanılır; sıçrayan sıvıya karşı korumalı " +
            "gövdesi vardır. Küçük boyutuna göre şaşırtıcı derecede dolgun bas verir."),
        ("ELK-003", "Taşınabilir Şarj Cihazı", "Elektronik", 749m,
            "Telefonu iki kez tam doldurur ve çantada neredeyse hiç yer kaplamaz. Uçuşlarda kabin " +
            "bagajıyla taşınabilecek kapasitededir."),
        ("EVA-001", "Çelik Termos", "Ev & Yaşam", 459m,
            "Sabah doldurulan içecek öğleden sonraya kadar sıcaklığını korur. Çift cidarlı gövdesi ve " +
            "sızdırmaz kapağı sayesinde çantada devrilse bile akıtmaz."),
        ("EVA-002", "Seramik Kupa Seti", "Ev & Yaşam", 329m,
            "Günlük kahvaltı sofrası için dört parçalık takım. Bulaşık makinesinde yıkanabilir, " +
            "desenleri zamanla solmaz."),
        ("GIY-001", "Pamuklu Tişört", "Giyim", 399m,
            "Sıcak havalarda nefes alan doğal kumaşı teri hapsetmez. Koyu tonları defalarca " +
            "yıkandıktan sonra bile rengini korur."),
        ("GIY-002", "Koşu Ayakkabısı", "Giyim", 1899m,
            "Asfaltta uzun mesafe için tasarlanmış yastıklama. Hafif tabanı sayesinde ayak bileğini " +
            "yormaz, uzun antrenmanlarda ağırlık hissettirmez."),
        ("OFS-001", "Ergonomik Klavye", "Ofis", 1149m,
            "Gün boyu yazı yazanlar için bilek desteği sunar. Tuşları sessizdir, açık ofiste yanındaki " +
            "kişiyi rahatsız etmez."),
    ];

    private static readonly (string Code, string City)[] Warehouses =
    [
        ("DEP-IST", "İstanbul"),
        ("DEP-ANK", "Ankara"),
        ("DEP-IZM", "İzmir"),
    ];

    /// <summary>
    /// Bumped whenever the schema or the seed changes. A file written by an older version is
    /// rebuilt rather than patched: this is generated sample data, so throwing it away costs
    /// nothing, and the alternative — CREATE TABLE IF NOT EXISTS over a file that already has the
    /// old shape — silently leaves the old columns in place and fails at the first query.
    /// </summary>
    private const long SchemaVersion = 2;

    /// <summary>Sales are seeded for this many days, ending the day before <see cref="SeedEnd"/>.</summary>
    private const int SeedDays = 120;

    /// <summary>Last day covered by the seeded sales.</summary>
    public static readonly DateOnly SeedEnd = new(2026, 9, 15);

    private readonly string _connectionString;
    private readonly ILogger<ShopDatabase> _logger;
    private readonly SemaphoreSlim _initialisationLock = new(1, 1);
    private bool _ready;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public ShopDatabase(IOptions<ShopOptions> options, ILogger<ShopDatabase> logger)
    {
        _logger = logger;

        var path = options.Value.DatabasePath;
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();
    }

    /// <summary>Opens a connection, creating and seeding the database on first use.</summary>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>An open connection the caller owns and must dispose.</returns>
    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken);

        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        if (_ready)
        {
            return;
        }

        await _initialisationLock.WaitAsync(cancellationToken);
        try
        {
            if (_ready)
            {
                return;
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var existingVersion = await ScalarAsync(connection, "PRAGMA user_version;", cancellationToken);
            if (existingVersion != SchemaVersion)
            {
                if (existingVersion != 0)
                {
                    _logger.LogInformation(
                        "Shop database is at schema {Old}, rebuilding for schema {New}.",
                        existingVersion, SchemaVersion);
                }

                await ExecuteAsync(connection,
                    """
                    DROP TABLE IF EXISTS sales;
                    DROP TABLE IF EXISTS stock;
                    DROP TABLE IF EXISTS warehouses;
                    DROP TABLE IF EXISTS products;
                    """, cancellationToken);
            }

            await ExecuteAsync(connection,
                """
                CREATE TABLE IF NOT EXISTS products (
                    code        TEXT PRIMARY KEY,
                    name        TEXT NOT NULL,
                    category    TEXT NOT NULL,
                    unit_price  REAL NOT NULL,
                    description TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS warehouses (
                    code TEXT PRIMARY KEY,
                    city TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS stock (
                    product_code   TEXT NOT NULL REFERENCES products(code),
                    warehouse_code TEXT NOT NULL REFERENCES warehouses(code),
                    quantity       INTEGER NOT NULL,
                    PRIMARY KEY (product_code, warehouse_code)
                );
                CREATE TABLE IF NOT EXISTS sales (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    product_code TEXT NOT NULL REFERENCES products(code),
                    quantity     INTEGER NOT NULL,
                    unit_price   REAL NOT NULL,
                    sold_on      TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_sales_sold_on ON sales(sold_on);
                """, cancellationToken);

            if (await ScalarAsync(connection, "SELECT COUNT(*) FROM products;", cancellationToken) > 0)
            {
                _ready = true;
                return;
            }

            await SeedAsync(connection, cancellationToken);

            // PRAGMA does not take parameters, and the value is a constant rather than input.
            await ExecuteAsync(connection, $"PRAGMA user_version = {SchemaVersion};", cancellationToken);
            _ready = true;

            _logger.LogInformation(
                "Seeded the sample shop database: {Products} products, {Warehouses} warehouses.",
                Products.Length, Warehouses.Length);
        }
        finally
        {
            _initialisationLock.Release();
        }
    }

    private static async Task SeedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var (code, name, category, price, description) in Products)
        {
            await ExecuteAsync(connection,
                "INSERT INTO products (code, name, category, unit_price, description) " +
                "VALUES ($c, $n, $k, $p, $d);",
                cancellationToken,
                ("$c", code), ("$n", name), ("$k", category), ("$p", (double)price), ("$d", description));
        }

        foreach (var (code, city) in Warehouses)
        {
            await ExecuteAsync(connection,
                "INSERT INTO warehouses (code, city) VALUES ($c, $t);",
                cancellationToken, ("$c", code), ("$t", city));
        }

        // Stock and sales come from a formula, not a random generator, so the numbers in the
        // README and in the tests stay true run after run.
        for (var p = 0; p < Products.Length; p++)
        {
            for (var w = 0; w < Warehouses.Length; w++)
            {
                var quantity = 12 + (p * 7 + w * 23) % 60;
                await ExecuteAsync(connection,
                    "INSERT INTO stock (product_code, warehouse_code, quantity) VALUES ($p, $w, $q);",
                    cancellationToken,
                    ("$p", Products[p].Code), ("$w", Warehouses[w].Code), ("$q", quantity));
            }
        }

        for (var day = 0; day < SeedDays; day++)
        {
            var date = SeedEnd.AddDays(-day);

            for (var p = 0; p < Products.Length; p++)
            {
                var quantity = (day * 7 + p * 3) % 5;
                if (quantity == 0)
                {
                    continue;
                }

                await ExecuteAsync(connection,
                    "INSERT INTO sales (product_code, quantity, unit_price, sold_on) VALUES ($p, $q, $u, $d);",
                    cancellationToken,
                    ("$p", Products[p].Code),
                    ("$q", quantity),
                    ("$u", (double)Products[p].Price),
                    ("$d", date.ToString("yyyy-MM-dd")));
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ScalarAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }
}
