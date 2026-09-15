using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using RagAgentLab.Configuration;

namespace RagAgentLab.Embeddings;

/// <summary>
/// <see cref="IVectorStore"/> backed by a Qdrant server.
/// <para>
/// This is the second implementation of the interface and exists to show that the
/// abstraction actually holds: nothing above it — not the ingestor, not the RAG pipeline, not
/// the agent's policy tool — changes when the store is swapped. Selecting it is a
/// configuration value (<c>Rag:VectorStore</c>), not a code change.
/// </para>
/// <para>
/// The differences from the in-memory store are the ones you would expect from a real
/// database: search is an approximate nearest-neighbour lookup over an HNSW index instead of
/// an exact scan, the data survives a restart, and the chunk text has to be carried in the
/// point payload because the store is no longer sharing the application's heap.
/// </para>
/// </summary>
public sealed class QdrantVectorStore : IVectorStore, IDisposable
{
    private const string ChunkIdField = "chunkId";
    private const string SourceNameField = "sourceName";
    private const string DocumentTitleField = "documentTitle";
    private const string ChunkIndexField = "chunkIndex";
    private const string TextField = "text";

    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly SemaphoreSlim _initialisationLock = new(1, 1);
    private bool _collectionReady;

    /// <summary>Creates a new instance. Called by the DI container.</summary>
    public QdrantVectorStore(IOptions<QdrantOptions> options, ILogger<QdrantVectorStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new QdrantClient(_options.Host, _options.Port, _options.UseHttps, _options.ApiKey);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(IReadOnlyList<VectorRecord> records, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return;
        }

        await EnsureCollectionAsync(cancellationToken);

        var points = records.Select(record => new PointStruct
        {
            Id = new PointId { Uuid = ToDeterministicUuid(record.Chunk.Id) },
            Vectors = record.Vector.ToArray(),
            Payload =
            {
                [ChunkIdField] = record.Chunk.Id,
                [SourceNameField] = record.Chunk.SourceName,
                [DocumentTitleField] = record.Chunk.DocumentTitle,
                [ChunkIndexField] = record.Chunk.ChunkIndex,
                [TextField] = record.Chunk.Text,
            },
        }).ToList();

        await _client.UpsertAsync(_options.CollectionName, points, cancellationToken: cancellationToken);
        _logger.LogDebug("Upserted {Count} point(s) into '{Collection}'.", points.Count, _options.CollectionName);
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

        await EnsureCollectionAsync(cancellationToken);

        var points = await _client.QueryAsync(
            _options.CollectionName,
            query: queryVector.ToArray(),
            limit: (ulong)topK,
            payloadSelector: true,
            cancellationToken: cancellationToken);

        return points.Select(point => new SearchResult(
            new DocumentChunk(
                Id: point.Payload[ChunkIdField].StringValue,
                SourceName: point.Payload[SourceNameField].StringValue,
                DocumentTitle: point.Payload[DocumentTitleField].StringValue,
                ChunkIndex: (int)point.Payload[ChunkIndexField].IntegerValue,
                Text: point.Payload[TextField].StringValue),
            point.Score)).ToArray();
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureCollectionAsync(cancellationToken);
        return (int)await _client.CountAsync(_options.CollectionName, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Creates the collection on first use. Qdrant needs the vector size and distance metric
    /// up front, unlike the in-memory store which infers both from whatever it is given.
    /// </summary>
    private async Task EnsureCollectionAsync(CancellationToken cancellationToken)
    {
        if (_collectionReady)
        {
            return;
        }

        await _initialisationLock.WaitAsync(cancellationToken);
        try
        {
            if (_collectionReady)
            {
                return;
            }

            if (!await _client.CollectionExistsAsync(_options.CollectionName, cancellationToken))
            {
                await _client.CreateCollectionAsync(
                    _options.CollectionName,
                    new VectorParams
                    {
                        Size = (ulong)_options.VectorSize,
                        // Cosine, to match the metric the in-memory store ranks by.
                        Distance = Distance.Cosine,
                    },
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Created Qdrant collection '{Collection}' with {Size} dimensions.",
                    _options.CollectionName, _options.VectorSize);
            }

            _collectionReady = true;
        }
        finally
        {
            _initialisationLock.Release();
        }
    }

    /// <summary>
    /// Turns a chunk id such as <c>01-izin-politikasi.txt#3</c> into a UUID.
    /// <para>
    /// Qdrant point ids must be an unsigned integer or a UUID, so the readable id cannot be
    /// used directly. Deriving the UUID from a hash of the id keeps upserts idempotent —
    /// re-ingesting the same document overwrites its points instead of duplicating them —
    /// and the original id is kept in the payload.
    /// </para>
    /// </summary>
    private static string ToDeterministicUuid(string chunkId)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(chunkId));
        return new Guid(hash).ToString();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client.Dispose();
        _initialisationLock.Dispose();
    }
}
