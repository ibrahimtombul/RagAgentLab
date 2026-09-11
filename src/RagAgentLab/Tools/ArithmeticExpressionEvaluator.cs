using System.Globalization;

namespace RagAgentLab.Tools;

/// <summary>
/// A small recursive-descent parser for arithmetic expressions.
/// <para>
/// Language models are notoriously unreliable at arithmetic, which is exactly why a
/// calculator makes a good first tool: the model decides <em>that</em> a calculation is
/// needed and what to compute, while the deterministic code decides <em>what the answer
/// is</em>. The parser is hand-written rather than delegating to something like
/// <c>DataTable.Compute</c> so that the accepted grammar is explicit and the failure
/// messages are useful to the model when it passes a malformed expression.
/// </para>
/// <para>
/// Grammar (lowest precedence first):
/// <code>
/// expression := term (('+' | '-') term)*
/// term       := unary (('*' | '/' | '%') unary)*
/// unary      := ('+' | '-')? power
/// power      := primary ('^' unary)?
/// primary    := number | '(' expression ')'
/// </code>
/// </para>
/// </summary>
public static class ArithmeticExpressionEvaluator
{
    /// <summary>Evaluates an arithmetic expression.</summary>
    /// <param name="expression">Expression such as <c>(14 + 6) * 900</c>.</param>
    /// <returns>The computed value.</returns>
    /// <exception cref="FormatException">The expression is malformed.</exception>
    /// <exception cref="DivideByZeroException">The expression divides by zero.</exception>
    public static double Evaluate(string expression)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expression);

        var position = 0;
        var value = ParseExpression(expression, ref position);

        SkipWhitespace(expression, ref position);
        if (position != expression.Length)
        {
            throw new FormatException($"Unexpected character '{expression[position]}' at position {position}.");
        }

        return value;
    }

    private static double ParseExpression(string text, ref int position)
    {
        var value = ParseTerm(text, ref position);

        while (true)
        {
            SkipWhitespace(text, ref position);
            if (position >= text.Length)
            {
                return value;
            }

            switch (text[position])
            {
                case '+':
                    position++;
                    value += ParseTerm(text, ref position);
                    break;
                case '-':
                    position++;
                    value -= ParseTerm(text, ref position);
                    break;
                default:
                    return value;
            }
        }
    }

    private static double ParseTerm(string text, ref int position)
    {
        var value = ParseUnary(text, ref position);

        while (true)
        {
            SkipWhitespace(text, ref position);
            if (position >= text.Length)
            {
                return value;
            }

            var op = text[position];
            if (op is not ('*' or '/' or '%' or 'x' or '×'))
            {
                return value;
            }

            position++;
            var right = ParseUnary(text, ref position);

            if (op is '/' or '%' && right == 0)
            {
                throw new DivideByZeroException("The expression divides by zero.");
            }

            value = op switch
            {
                '/' => value / right,
                '%' => value % right,
                _ => value * right,
            };
        }
    }

    private static double ParseUnary(string text, ref int position)
    {
        SkipWhitespace(text, ref position);

        if (position < text.Length && text[position] is '-' or '+')
        {
            var sign = text[position] == '-' ? -1 : 1;
            position++;
            return sign * ParseUnary(text, ref position);
        }

        return ParsePower(text, ref position);
    }

    private static double ParsePower(string text, ref int position)
    {
        var value = ParsePrimary(text, ref position);

        SkipWhitespace(text, ref position);
        if (position < text.Length && text[position] == '^')
        {
            position++;
            // Right-associative: 2^3^2 is 2^(3^2).
            return Math.Pow(value, ParseUnary(text, ref position));
        }

        return value;
    }

    private static double ParsePrimary(string text, ref int position)
    {
        SkipWhitespace(text, ref position);

        if (position >= text.Length)
        {
            throw new FormatException("Unexpected end of expression.");
        }

        if (text[position] == '(')
        {
            position++;
            var inner = ParseExpression(text, ref position);

            SkipWhitespace(text, ref position);
            if (position >= text.Length || text[position] != ')')
            {
                throw new FormatException("Missing closing parenthesis.");
            }

            position++;
            return inner;
        }

        var start = position;
        while (position < text.Length && (char.IsAsciiDigit(text[position]) || text[position] is '.' or ','))
        {
            position++;
        }

        if (start == position)
        {
            throw new FormatException($"Expected a number at position {start}, found '{text[start]}'.");
        }

        // Accept a comma as a decimal separator, since Turkish input often uses it.
        var literal = text[start..position].Replace(',', '.');
        return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new FormatException($"'{literal}' is not a valid number.");
    }

    private static void SkipWhitespace(string text, ref int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }
    }
}
