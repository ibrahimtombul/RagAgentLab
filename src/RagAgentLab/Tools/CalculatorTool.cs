using System.ComponentModel;
using System.Globalization;
using Microsoft.SemanticKernel;

namespace RagAgentLab.Tools;

/// <summary>
/// Calculator tool exposed to the agent.
/// <para>
/// The <see cref="KernelFunctionAttribute"/> and <see cref="DescriptionAttribute"/> markers
/// are what Semantic Kernel turns into a JSON tool schema for the model. The descriptions
/// are therefore not documentation for humans but the actual prompt the model reads when it
/// decides whether to call this function — which is why they state <em>when</em> to use the
/// tool, not just what it does.
/// </para>
/// </summary>
public sealed class CalculatorTool
{
    /// <summary>Evaluates an arithmetic expression and returns the result.</summary>
    /// <param name="expression">The expression to evaluate.</param>
    /// <returns>The result, or a human-readable error the model can recover from.</returns>
    [KernelFunction("calculate")]
    [Description("Evaluates an arithmetic expression and returns the exact numeric result. " +
                 "Always use this instead of doing arithmetic yourself whenever the answer requires " +
                 "adding, subtracting, multiplying, dividing or taking a percentage of numbers.")]
    public string Calculate(
        [Description("Arithmetic expression using numbers and the operators + - * / % ^ and parentheses, " +
                     "for example '(3 * 4500) + (3 * 900)'. Do not include currency symbols or text.")]
        string expression)
    {
        try
        {
            var result = ArithmeticExpressionEvaluator.Evaluate(expression);
            return result.ToString("0.####", CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or DivideByZeroException or ArgumentException)
        {
            // Returned rather than thrown: the model can read the message and retry with a
            // corrected expression, which is the whole point of a tool-calling loop.
            return $"ERROR: {ex.Message}";
        }
    }
}
