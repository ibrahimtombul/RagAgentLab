using System.Buffers.Binary;
using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RagAgentLab.Configuration;

namespace RagAgentLab.Embeddings;

/// <summary>
/// Vector store that keeps its data in a SQLite file and a matching in-memory index.
/// <para>
/// SQLite has no vector type and no nearest-neighbour index, so search is still the exact
/// brute-force scan the in-memory store performs — the vectors are simply loaded once at
/// start-up and kept in memory, with SQLite acting as the durable copy. That split is the
/// whole point: search stays as fast and as exact as before, while anything written survives
/// a restart, which is what makes notes added from the chat worth adding at all.
/// </para>
/// <para>
/// It is the right shape up to a few tens of thousands of chunks. Past that the scan, and
/// holding every vector in memory, both stop being reasonable and a real vector database
/// (the Qdrant implementation next door) takes over — without anything above
/// <see cref="IVectorStore"/> changing.
/// </para>
/// </summary>
public sealed class SqliteVectorStore : IVectorStore, IFingerprintedVectorStore, IDisposable
{
    private readonly ConcurrentDictionary<string, VectorRecord> _index = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _fingerprints = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _connectionString;
    private readonly ILogger<SqliteVectorStore> _logger;
    private bool _initialised;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public SqliteVectorStore(IOptions<SqliteOptions> options, ILogger<SqliteVectorStore> logger)
    {
        _logger = logger;

        var path = options.Value.DatabasePath;
        var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath }.ToString();

        _logger.LogInformation("Vector store database: {Path}", fullPath);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        await EnsureInitialisedAsync(cancellationToken);
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            foreach (var record in records)
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO chunks (id, source_name, document_title, chunk_index, text, fingerprint, origin, vector)
                    VALUES ($id, $source, $title, $index, $text, $fingerprint, $origin, $vector)
                    ON CONFLICT(id) DO UPDATE SET
                        source_name = excluded.source_name,
                        document_title = excluded.document_title,
                        chunk_index = excluded.chunk_index,
                        text = excluded.text,
                        fingerprint = excluded.fingerprint,
                        origin = excluded.origin,
                        vector = excluded.vector;
                    """;

                var fingerprint = record.Fingerprint;

                command.Parameters.AddWithValue("$id", record.Chunk.Id);
                command.Parameters.AddWithValue("$source", record.Chunk.SourceName);
                command.Parameters.AddWithValue("$title", record.Chunk.DocumentTitle);
                command.Parameters.AddWithValue("$index", record.Chunk.ChunkIndex);
                command.Parameters.AddWithValue("$text", record.Chunk.Text);
                command.Parameters.AddWithValue("$fingerprint", fingerprint);
                command.Parameters.AddWithValue("$origin", record.Chunk.Origin.ToString());
                command.Parameters.AddWithValue("$vector", ToBlob(record.Vector));

                await command.ExecuteNonQueryAsync(cancellationToken);

                _index[record.Chunk.Id] = record;
                _fingerprints[record.Chunk.Id] = fingerprint;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        _logger.LogDebug("Stored {Count} chunk(s).", records.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        ReadOnlyMemory<float> queryVector,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), topK, "topK must be greater than zero.");
        }

        await EnsureInitialisedAsync(cancellationToken);

        var hits = _index.Values
            .Select(record => new SearchResult(record.Chunk, VectorMath.CosineSimilarity(queryVector, record.Vector)))
            .OrderByDescending(hit => hit.Score)
            .Take(topK)
            .ToArray();

        return hits;
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitialisedAsync(cancellationToken);
        return _index.Count;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetFingerprintsAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitialisedAsync(cancellationToken);
        return _fingerprints.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DocumentChunk>> GetChunksAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitialisedAsync(cancellationToken);
        return _index.Values.Select(record => record.Chunk).ToArray();
    }

    /// <inheritdoc />
    public async Task<int> RemoveChunksNotInAsync(
        IReadOnlyCollection<string> chunkIdsToKeep,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunkIdsToKeep);
        await EnsureInitialisedAsync(cancellationToken);

        var keep = new HashSet<string>(chunkIdsToKeep, StringComparer.Ordinal);

        // Only file chunks are swept: a note typed into the chat has no document behind it,
        // so re-ingesting the data directory must never take it with them.
        var stale = _index.Values
            .Where(record => record.Chunk.Origin == ChunkOrigin.File && !keep.Contains(record.Chunk.Id))
            .Select(record => record.Chunk.Id)
            .ToArray();
        if (stale.Length == 0)
        {
            return 0;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            foreach (var id in stale)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM chunks WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id);
                await command.ExecuteNonQueryAsync(cancellationToken);

                _index.TryRemove(id, out _);
                _fingerprints.TryRemove(id, out _);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }

        _logger.LogInformation("Removed {Count} stale chunk(s).", stale.Length);
        return stale.Length;
    }

    /// <summary>Creates the table if needed and loads every stored vector into memory.</summary>
    private async Task EnsureInitialisedAsync(CancellationToken cancellationToken)
    {
        if (_initialised)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialised)
            {
                return;
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using (var create = connection.CreateCommand())
            {
                create.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS chunks (
                        id              TEXT PRIMARY KEY,
                        source_name     TEXT NOT NULL,
                        document_title  TEXT NOT NULL,
                        chunk_index     INTEGER NOT NULL,
                        text            TEXT NOT NULL,
                        fingerprint     TEXT NOT NULL,
                        origin          TEXT NOT NULL DEFAULT 'File',
                        vector          BLOB NOT NULL
                    );
                    """;
                await create.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var read = connection.CreateCommand())
            {
                read.CommandText =
                    "SELECT id, source_name, document_title, chunk_index, text, fingerprint, vector, origin FROM chunks;";

                await using var reader = await read.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var chunk = new DocumentChunk(
                        Id: reader.GetString(0),
                        SourceName: reader.GetString(1),
                        DocumentTitle: reader.GetString(2),
                        ChunkIndex: reader.GetInt32(3),
                        Text: reader.GetString(4))
                    {
                        Origin = Enum.TryParse<ChunkOrigin>(reader.GetString(7), out var origin)
                            ? origin
                            : ChunkOrigin.File,
                    };

                    _index[chunk.Id] = new VectorRecord(chunk, FromBlob((byte[])reader[6]));
                    _fingerprints[chunk.Id] = reader.GetString(5);
                }
            }

            _initialised = true;
            _logger.LogInformation("Loaded {Count} chunk(s) from the database.", _index.Count);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>Packs a vector as little-endian float32, the layout <see cref="FromBlob"/> expects.</summary>
    private static byte[] ToBlob(ReadOnlyMemory<float> vector)
    {
        var blob = new byte[vector.Length * sizeof(float)];
        var span = vector.Span;

        for (var i = 0; i < span.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(blob.AsSpan(i * sizeof(float)), span[i]);
        }

        return blob;
    }

    /// <summary>Unpacks a vector written by <see cref="ToBlob"/>.</summary>
    private static ReadOnlyMemory<float> FromBlob(byte[] blob)
    {
        var vector = new float[blob.Length / sizeof(float)];

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = BinaryPrimitives.ReadSingleLittleEndian(blob.AsSpan(i * sizeof(float)));
        }

        return vector;
    }

    /// <inheritdoc />
    public void Dispose() => _writeLock.Dispose();
}
