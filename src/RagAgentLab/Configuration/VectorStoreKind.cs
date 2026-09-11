namespace RagAgentLab.Configuration;

/// <summary>Available <c>IVectorStore</c> implementations.</summary>
public enum VectorStoreKind
{
    /// <summary>Brute-force exact search over a list held in process memory. No infrastructure.</summary>
    InMemory = 0,

    /// <summary>Approximate nearest-neighbour search in a Qdrant server. Survives restarts.</summary>
    Qdrant = 1,
}
