namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// What the pages draw alike: the label of the page at the top left, the name of the program in the top row, and
/// the key hints of the bottom line. Adapted from Blyant's Menu.cs.
/// </summary>
internal static class Menu
{
    internal const string ApplicationName = "Blyant Validator";

    /// <summary>
    /// The label of the page at the top left ("FILES")
    /// </summary>
    /// <param name="attention">True to show the label in the attention colors</param>
    public static void WriteModeLabel(string title)
    {
        Tools.SetCursor(0, 0);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.HighlightInverted);
        Console.Write(" " + Tools.TruncateString(title.ToUpperInvariant(), CaptionRoom, true) + " ");
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    /// <summary>
    /// The room for a caption at the left of the top row: up to a column before the name of the program, which is
    /// written in the middle
    /// </summary>
    private static int CaptionRoom => Math.Max(10, Screen.LineWidth / 2 - (ApplicationName.Length / 2 + 1) - 3);

    internal static void WriteNameInTopBar()
    {
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.HeaderColor);
        Tools.SetCursor(0, Screen.LineWidth / 2 - (ApplicationName.Length / 2 + 1));
        Console.Write(" " + ApplicationName + " ");
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    /// <summary>
    /// Write a line of keys and what they do ("j/k Select | Esc Close"), with the keys highlighted. The hints that do
    /// not fit are left out
    /// </summary>
    internal static void WriteKeyHints(string hints)
    {
        Console.Write(" ");
        var room = Screen.LineWidth - Console.CursorLeft;
        foreach (var hint in hints.Split(" | "))
        {
            var space = hint.IndexOf(' ');
            var key = " " + (space < 0 ? hint : hint[..space]) + " ";
            // One blank space on each side of the description
            var description = space < 0 ? " " : " " + hint[(space + 1)..] + " ";
            room -= TextMetrics.WidthOf(key + description);
            if (room < 0) break;
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.KeyHintInverted);
            Console.Write(key);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.KeyHint);
            Console.Write(description);
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }
}
