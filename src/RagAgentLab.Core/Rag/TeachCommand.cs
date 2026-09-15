namespace RagAgentLab.Rag;

/// <summary>
/// Recognises the chat command that writes a note into the knowledge base.
/// <para>
/// It accepts several spellings on purpose. The hint in the UI shows <c>/ogren</c>, but a
/// Turkish keyboard produces <c>/öğren</c> without the writer thinking about it, and that is
/// exactly what a user typed the first time this was tried. An unrecognised command is not
/// rejected — it falls through and is sent to the model as an ordinary question, so the note is
/// silently not stored and the answer is nonsense. Accepting the spellings people actually type
/// is cheaper than explaining the one that works.
/// </para>
/// </summary>
public static class TeachCommand
{
    /// <summary>The spelling shown in the UI.</summary>
    public const string Canonical = "/ogren";

    /// <summary>Every accepted spelling, longest first so a prefix cannot shadow a longer one.</summary>
    public static readonly IReadOnlyList<string> Aliases =
    [
        "/öğren", "/ogren", "/öğret", "/ogret", "/learn", "/teach",
    ];

    /// <summary>Checks whether a message is a teach command and extracts the note.</summary>
    /// <param name="message">The raw message the user typed.</param>
    /// <param name="note">The text after the command, trimmed. Empty when this is not a command.</param>
    /// <returns>True when the message starts with one of <see cref="Aliases"/>.</returns>
    public static bool TryParse(string? message, out string note)
    {
        note = string.Empty;

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        var trimmed = message.TrimStart();

        foreach (var alias in Aliases)
        {
            if (!trimmed.StartsWith(alias, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rest = trimmed[alias.Length..];

            // "/ogrenmek istiyorum" is a sentence, not a command: the alias has to be a whole word.
            if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]))
            {
                continue;
            }

            note = rest.Trim();
            return true;
        }

        return false;
    }
}
