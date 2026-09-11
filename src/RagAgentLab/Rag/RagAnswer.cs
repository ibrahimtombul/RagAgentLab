using RagAgentLab.Embeddings;

namespace RagAgentLab.Rag;

/// <summary>
/// The result of one RAG round-trip, including the retrieved sources so the console (and,
/// in a real product, the UI) can show the user where the answer came from.
/// </summary>
/// <param name="Question">The question that was asked.</param>
/// <param name="Answer">The grounded answer produced by the model.</param>
/// <param name="Sources">Chunks that were injected into the prompt, best match first.</param>
/// <param name="Duration">Wall-clock time for retrieval plus generation.</param>
public sealed record RagAnswer(
    string Question,
    string Answer,
    IReadOnlyList<SearchResult> Sources,
    TimeSpan Duration);
