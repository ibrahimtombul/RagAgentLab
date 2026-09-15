namespace RagAgentLab.Configuration;

/// <summary>Available <c>IVectorStore</c> implementations.</summary>
public enum VectorStoreKind
{
    /// <summary>Brute-force exact search over a list held in process memory. No infrastructure.</summary>
    InMemory = 0,

    /// <summary>
    /// Exact search over vectors held in memory, with a SQLite file as the durable copy.
    /// Survives restarts and needs no server.
    /// </summary>
    Sqlite = 1,

    /// <summary>Approximate nearest-neighbour search in a Qdrant server. Survives restarts.</summary>
    Qdrant = 2,
}
