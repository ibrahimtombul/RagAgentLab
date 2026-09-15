using System.ComponentModel;
using System.Text;
using Microsoft.SemanticKernel;
using RagAgentLab.Rag;

namespace RagAgentLab.Tools;

/// <summary>
/// Exposes the stage 2 retrieval step as a tool the agent can call.
/// <para>
/// This is the bridge between the two halves of the project. In stage 2 retrieval always ran:
/// every question triggered a vector search whether or not it needed one. Here the model
/// decides, which is the actual difference between a RAG pipeline and an agent — and it means
/// "what is 15% of 3500?" no longer performs a pointless embedding call.
/// </para>
/// <para>
/// The tool deliberately returns raw excerpts rather than a finished answer: composing them
/// into a reply is the agent's job, and returning the source names lets it cite them.
/// </para>
/// </summary>
public sealed class HrPolicyTool
{
    private readonly IRagPipeline _ragPipeline;

    /// <summary>Creates a new instance. Resolved from DI by Semantic Kernel.</summary>
    public HrPolicyTool(IRagPipeline ragPipeline) => _ragPipeline = ragPipeline;

    /// <summary>Searches the HR policy documents for passages relevant to a question.</summary>
    /// <param name="question">What to look for, in natural language.</param>
    /// <param name="cancellationToken">Token used to cancel the search.</param>
    /// <returns>The most relevant excerpts, each labelled with its source file.</returns>
    [KernelFunction("search_hr_policy")]
    // The description is the only thing that tells the model what this corpus contains, so it
    // has to be kept in step with the documents themselves. A statutory minimum-wage table was
    // once added to the corpus without listing it here, and the model stopped calling the tool
    // for wage questions entirely - nothing told it the answer might be in there. That table
    // now lives behind get_minimum_wage instead, because it is a lookup rather than prose.
    [Description("Searches the company's internal HR policy documents (annual leave, sick leave, " +
                 "parental leave, hybrid and remote work, travel and expense limits, meal and travel " +
                 "allowances, training budget, performance reviews and promotions), plus any note the " +
                 "user has taught the assistant, and returns the most relevant excerpts. " +
                 "Use this for any question about company rules, limits, amounts, deadlines or processes. " +
                 "Never answer such questions from your own knowledge.")]
    public async Task<string> SearchAsync(
        [Description("The question or topic to look up, in the user's own words.")] string question,
        CancellationToken cancellationToken = default)
    {
        var hits = await _ragPipeline.RetrieveAsync(question, cancellationToken: cancellationToken);

        if (hits.Count == 0)
        {
            return "No relevant passage was found in the HR policy documents.";
        }

        var builder = new StringBuilder();
        foreach (var hit in hits)
        {
            builder.AppendLine($"--- source: {hit.Chunk.SourceName} (similarity {hit.Score:F3}) ---");
            builder.AppendLine(hit.Chunk.ToContextualText());
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }
}
