namespace RagAgentLab.Agents;

/// <summary>Who produced a message in a conversation.</summary>
public enum ConversationRole
{
    /// <summary>A message typed by the person using the assistant.</summary>
    User = 0,

    /// <summary>A message the assistant produced.</summary>
    Assistant = 1,
}

/// <summary>
/// One turn of a conversation, in a form that does not leak Semantic Kernel types into the
/// hosts. A web UI holds a list of these per chat session and hands it to the agent on every
/// turn, which is what makes follow-up questions ("peki ya yurt dışında?") work.
/// </summary>
/// <param name="Role">Who produced the message.</param>
/// <param name="Content">The message text.</param>
public sealed record ConversationMessage(ConversationRole Role, string Content);
