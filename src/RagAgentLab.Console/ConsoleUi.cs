// Physically part of the console host, but the namespace deliberately stays
// RagAgentLab.Infrastructure: a namespace called RagAgentLab.Console would shadow
// System.Console for every file in this project.
namespace RagAgentLab.Infrastructure;

/// <summary>
/// Small helper for readable, colour-coded console output. The agent stage prints a
/// step-by-step trace (which tool was chosen, what it returned, what the final answer
/// was), and that trace is the main thing a reviewer looks at, so it is worth
/// formatting properly instead of using raw Console.WriteLine everywhere.
/// </summary>
public static class ConsoleUi
{
    /// <summary>Prints a titled section separator.</summary>
    public static void Section(string title)
    {
        Console.WriteLine();
        Write(ConsoleColor.Cyan, $"== {title} ==");
        Console.WriteLine();
    }

    /// <summary>Prints a numbered/labelled step of a pipeline.</summary>
    public static void Step(string label, string message) =>
        Write(ConsoleColor.DarkCyan, $"  -> {label}: ", message);

    /// <summary>Prints an informational line.</summary>
    public static void Info(string message) => Console.WriteLine($"  {message}");

    /// <summary>Prints a success line.</summary>
    public static void Success(string message) => Write(ConsoleColor.Green, $"  [ok] {message}");

    /// <summary>Prints a warning line.</summary>
    public static void Warn(string message) => Write(ConsoleColor.Yellow, $"  [!] {message}");

    /// <summary>Prints an error line.</summary>
    public static void Error(string message) => Write(ConsoleColor.Red, $"  [x] {message}");

    /// <summary>Prints the model's answer in a visually distinct block.</summary>
    public static void Answer(string message)
    {
        Console.WriteLine();
        Write(ConsoleColor.White, "  ANSWER");
        Console.WriteLine($"  {message.Replace("\n", "\n  ")}");
        Console.WriteLine();
    }

    private static void Write(ConsoleColor color, string coloredText, string? plainText = null)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(coloredText);
        Console.ForegroundColor = previous;
        Console.WriteLine(plainText ?? string.Empty);
    }
}
