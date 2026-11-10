namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// Writing to the terminal: the cursor, clearing the screen, and texts cut to fit.
/// Adapted from Blyant's Tools.cs.
/// </summary>
internal static class Tools
{
    /// <summary>
    /// A text cut to a length, for the screen: control characters (as an escape code in a file name) are blank
    /// spaces, so they are never sent to the terminal
    /// </summary>
    /// <param name="s">The text</param>
    /// <param name="length">The number of terminal cells it may take (a wide character takes two)</param>
    /// <param name="dots">True to end a text that was cut with "..."</param>
    public static string TruncateString(string s, int length, bool dots)
    {
        if (string.IsNullOrEmpty(s) || length <= 0) return string.Empty;
        s = TextMetrics.Printable(s);
        if (TextMetrics.WidthOf(s) <= length) return s;
        return dots && length > 3 ? TextMetrics.StartPart(s, length - 3) + "..." : TextMetrics.StartPart(s, length);
    }

    /// <summary>
    /// A text cut to fit on a line of the window after a number of columns already used (writing past the last column
    /// of the bottom line would scroll the window). Key hints ("a | b | c") lose whole hints from the end
    /// </summary>
    internal static string FitLine(string text, int used)
    {
        var room = Math.Max(0, Screen.LineWidth - 1 - used);
        while (TextMetrics.WidthOf(text) > room && text.LastIndexOf(" | ", StringComparison.Ordinal) > 0)
        {
            text = text[..text.LastIndexOf(" | ", StringComparison.Ordinal)];
        }

        while (TextMetrics.WidthOf(text) > room)
        {
            text = text[..^TextMetrics.PreviousElementLength(text, text.Length)];
        }

        return text;
    }

    internal static void ClearScreen()
    {
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        Console.Clear();
        ChoiceBox.Forget();
    }

    public static void ShowCursor()
    {
        try
        {
            Console.CursorVisible = true;
        }
        catch
        {
            // Do nothing
        }
    }

    public static void HideCursor()
    {
        try
        {
            Console.CursorVisible = false;
        }
        catch
        {
            // Do nothing
        }
    }

    private static bool _redrawRequested;

    /// <summary>
    /// Move the cursor. A place outside the window (it was made smaller while a page was drawn) is not written to: the
    /// page is drawn again (TakeRedrawRequest)
    /// </summary>
    public static void SetCursor(int top, int left)
    {
        try
        {
            // On Linux and macOS a place outside the window is not refused (the terminal would scroll): it is told
            // as on Windows
            if (!OperatingSystem.IsWindows() && IsOutsideWindow(left, top))
            {
                throw new ArgumentOutOfRangeException(nameof(top));
            }

            Console.SetCursorPosition(left, top);
        }
        catch
        {
            _redrawRequested = true;
        }
    }

    // True if a place is outside the terminal window (false if its size is not known)
    private static bool IsOutsideWindow(int column, int row)
    {
        try
        {
            var width = Console.WindowWidth;
            var height = Console.WindowHeight;
            return width > 0 && height > 0 && (column >= width || row >= height);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// True (once) if a page could not be drawn because the terminal window was made smaller, and must be drawn again
    /// with the dimensions that are now in use
    /// </summary>
    internal static bool TakeRedrawRequest()
    {
        var requested = _redrawRequested;
        _redrawRequested = false;
        if (requested) Screen.ApplyAutoSize();
        return requested;
    }

    /// <summary>
    /// Clear the bottom line and write a text on it
    /// </summary>
    internal static void WriteBottomLine(string text, ConsoleColors.ColorPreset preset, bool fillLine)
    {
        SetCursor(Screen.LineCount - 1, 0);
        ConsoleColors.SetColors(fillLine ? preset : ConsoleColors.ColorPreset.Normal);
        Console.Write(new string(' ', Screen.LineWidth - 1));
        SetCursor(Screen.LineCount - 1, 0);
        ConsoleColors.SetColors(preset);
        Console.Write(text);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }
}
