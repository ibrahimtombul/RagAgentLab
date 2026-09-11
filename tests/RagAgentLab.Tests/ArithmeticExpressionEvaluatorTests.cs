using RagAgentLab.Tools;

namespace RagAgentLab.Tests;

/// <summary>
/// Tests for the calculator behind the agent's <c>calculate</c> tool. This is deterministic
/// code the model delegates to precisely because it must be exact, so it is worth pinning
/// down — including the error paths, which the model reads and recovers from.
/// </summary>
public sealed class ArithmeticExpressionEvaluatorTests
{
    [Theory]
    [InlineData("2+3", 5)]
    [InlineData("750 * 12", 9000)]
    [InlineData("4720 / 1.18", 4000)]
    [InlineData("(3 * 4500) + (3 * 900)", 16200)]
    [InlineData("10 - 2 - 3", 5)]               // left-associative subtraction
    [InlineData("2 + 3 * 4", 14)]               // multiplication binds tighter
    [InlineData("(2 + 3) * 4", 20)]
    [InlineData("-5 + 8", 3)]
    [InlineData("2 ^ 3 ^ 2", 512)]              // exponentiation is right-associative
    [InlineData("17 % 5", 2)]
    [InlineData("1.5 * 4", 6)]
    [InlineData("1,5 * 4", 6)]                  // comma accepted as a decimal separator
    public void Evaluate_ComputesExpectedValue(string expression, double expected) =>
        Assert.Equal(expected, ArithmeticExpressionEvaluator.Evaluate(expression), precision: 6);

    [Theory]
    [InlineData("2 +")]
    [InlineData("(2 + 3")]
    [InlineData("2 ** 3")]
    [InlineData("iki + üç")]
    [InlineData("2 + 3 TL")]
    public void Evaluate_ThrowsOnMalformedExpression(string expression) =>
        Assert.Throws<FormatException>(() => ArithmeticExpressionEvaluator.Evaluate(expression));

    [Fact]
    public void Evaluate_ThrowsOnDivisionByZero() =>
        Assert.Throws<DivideByZeroException>(() => ArithmeticExpressionEvaluator.Evaluate("5 / 0"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Evaluate_ThrowsOnEmptyInput(string expression) =>
        Assert.Throws<ArgumentException>(() => ArithmeticExpressionEvaluator.Evaluate(expression));

    [Fact]
    public void CalculatorTool_ReturnsErrorTextInsteadOfThrowing()
    {
        // The tool must not throw: the model reads the error and retries with a fixed
        // expression, which is the point of the tool-calling loop.
        var result = new CalculatorTool().Calculate("2 +");

        Assert.StartsWith("ERROR:", result);
    }

    [Fact]
    public void CalculatorTool_FormatsResultWithInvariantCulture()
    {
        var result = new CalculatorTool().Calculate("4720 / 1.18");

        Assert.Equal("4000", result);
    }
}
