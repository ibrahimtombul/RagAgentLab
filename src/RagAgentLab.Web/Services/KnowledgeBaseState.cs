namespace RagAgentLab.Web.Services;

/// <summary>Where the knowledge base is in its start-up ingestion.</summary>
public enum KnowledgeBaseStatus
{
    /// <summary>Ingestion has not finished yet.</summary>
    Loading = 0,

    /// <summary>The corpus is indexed and questions can be answered.</summary>
    Ready = 1,

    /// <summary>Ingestion failed; <see cref="KnowledgeBaseState.Error"/> says why.</summary>
    Failed = 2,
}

/// <summary>
/// Shared, singleton view of the knowledge base's readiness.
/// <para>
/// Ingestion needs the embedding model, so it can only fail for the same reasons the model
/// server can (not running, model not pulled). Surfacing that as state rather than letting the
/// web app fail to start means the UI can show a useful message instead of a blank page.
/// </para>
/// </summary>
public sealed class KnowledgeBaseState
{
    /// <summary>Current status.</summary>
    public KnowledgeBaseStatus Status { get; private set; } = KnowledgeBaseStatus.Loading;

    /// <summary>Number of chunks indexed, once ready.</summary>
    public int ChunkCount { get; private set; }

    /// <summary>Number of source documents indexed, once ready.</summary>
    public int DocumentCount { get; private set; }

    /// <summary>Why ingestion failed, when it did.</summary>
    public string? Error { get; private set; }

    /// <summary>Raised whenever the status changes, so open pages can re-render.</summary>
    public event Action? Changed;

    /// <summary>Marks the knowledge base as ready.</summary>
    /// <param name="documentCount">Documents indexed.</param>
    /// <param name="chunkCount">Chunks indexed.</param>
    public void MarkReady(int documentCount, int chunkCount)
    {
        DocumentCount = documentCount;
        ChunkCount = chunkCount;
        Status = KnowledgeBaseStatus.Ready;
        Changed?.Invoke();
    }

    /// <summary>Records that a note was added at runtime, so the header count stays truthful.</summary>
    /// <param name="chunkCount">How many chunks the note produced.</param>
    public void NoteAdded(int chunkCount)
    {
        ChunkCount += chunkCount;
        Changed?.Invoke();
    }

    /// <summary>Marks ingestion as failed.</summary>
    /// <param name="error">A message the user can act on.</param>
    public void MarkFailed(string error)
    {
        Error = error;
        Status = KnowledgeBaseStatus.Failed;
        Changed?.Invoke();
    }
}
