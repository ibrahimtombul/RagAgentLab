using System.Text.RegularExpressions;

namespace RagAgentLab.Agents;

/// <summary>
/// Detects a known failure mode of small local models: writing the tool call they wanted to
/// make as ordinary prose instead of emitting it through the tool-calling channel.
/// <para>
/// When that happens the SDK never sees a call, so no tool runs, and the raw text surfaces
/// as the answer — the user sees something like
/// <c>search_hr_policy({"question": "..."})</c> and has no idea what went wrong. It is a
/// model capability limit rather than a defect in the wiring, and it disappears with a model
/// better trained on tool use, but silence about it looks like an application bug.
/// </para>
/// <para>
/// The check lives here, beside the agent, rather than in a UI: both the console host and the
/// web host need it, and it is a property of the model's output, not of how it is displayed.
/// </para>
/// </summary>
public static class AgentOutputHeuristics
{
    /// <summary>Matches <c>toolName({ ... })</c> and <c>toolName(...)</c> at the start of the text.</summary>
    private static readonly Regex CallShapedPrefix = new(
        @"^[""'`\s]*[A-Za-z_][\w.\-]*\s*\(\s*[{""]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Returns true when the text looks like a tool invocation rather than an answer.
    /// </summary>
    /// <param name="answer">The model's reply.</param>
    /// <param name="toolNames">
    /// Names of the tools the agent offered, used to catch the case where the model simply
    /// echoes a tool name. May be empty.
    /// </param>
    /// <returns>True when the reply appears to be a tool call written as text.</returns>
    public static bool LooksLikeTextualToolCall(string answer, IEnumerable<string>? toolNames = null)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var trimmed = answer.TrimStart();

        // A bare JSON object carrying a function name, e.g. {"name":"hr-search_hr_policy",...}
        if (trimmed.StartsWith('{') &&
            (trimmed.Contains("\"name\"", StringComparison.Ordinal) ||
             trimmed.Contains("\"function\"", StringComparison.Ordinal) ||
             trimmed.Contains("\"parameters\"", StringComparison.Ordinal) ||
             trimmed.Contains("\"arguments\"", StringComparison.Ordinal)))
        {
            return true;
        }

        // A call-shaped prefix, e.g. search_hr_policy({"question": "..."})
        if (CallShapedPrefix.IsMatch(trimmed))
        {
            return true;
        }

        if (toolNames is null)
        {
            return false;
        }

        var names = toolNames.ToArray();

        // The reply is little more than a tool's name.
        if (names.Any(name =>
                trimmed.StartsWith(name, StringComparison.OrdinalIgnoreCase) &&
                trimmed.Length < name.Length + 200))
        {
            return true;
        }

        // The model announced the call and cited the tool as though it were a source:
        // "Güncel asgari ücret bilgileri için arama yapalım. [wage-get_minimum_wage]".
        // The instruction to cite sources in square brackets appears to be what it is
        // generalising from; either way no tool ran, so the answer is not grounded in anything.
        return names.Any(name => trimmed.Contains($"[{name}]", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The notice appended to such a reply so the user sees what happened.</summary>
    public const string TextualToolCallNotice =
        "\n\n[Not: model, aracı gerçekten çağırmak yerine çağrıyı metin olarak yazdı; " +
        "bu yüzden hiçbir araç çalışmadı. Soruyu yeniden ifade etmeyi veya daha büyük bir " +
        "model kullanmayı deneyin.]";
}
