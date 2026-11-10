namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// A box drawn on top of the page, which is not cleared: for asking before something is done, or for a message.
/// The caller draws the page again when the box is closed. A box that follows another box (the page has not been
/// drawn again in between) covers the one before it. Adapted from Blyant's ChoiceBox.cs.
/// </summary>
internal static class ChoiceBox
{
    private const int MinInner = 40; // The narrowest box (inside the frame)
    private const string ChoiceKeys = "j/k Select | Enter Choose | Esc Cancel";
    private const string AnyKey = "[Press any key to continue]";

    // Where the last box is (its frame), until the screen is cleared
    private static (int Top, int Left, int Right, int Bottom)? _shown;

    /// <summary>
    /// The screen has been cleared: no box is shown
    /// </summary>
    internal static void Forget() => _shown = null;

    /// <summary>
    /// Ask yes or no. Y and N answer at once, j/k select and Enter picks, Esc is no
    /// </summary>
    /// <param name="warning">True for something that cannot be undone: the question is shown as a warning, and No is
    /// selected from the start</param>
    /// <returns>True for yes</returns>
    internal static bool Confirm(string caption, string question, string note, bool warning)
    {
        return Pick(caption, question, note, ["Yes", "No"], ["Y", "N"], warning ? 1 : 0, warning) == 0;
    }

    /// <summary>
    /// Pick one of a few choices. The key of each choice is shown in front of it and picks it at once
    /// </summary>
    /// <returns>The number of the choice, or -1 if the box was closed with Esc</returns>
    internal static int Pick(string caption, string question, string note, IReadOnlyList<string> choices,
        IReadOnlyList<string> keys, int selected, bool warning)
    {
        var keyWidth = keys.Max(TextMetrics.WidthOf);
        var widest = Math.Max(TextMetrics.WidthOf(caption) + 4,
            Math.Max(TextMetrics.WidthOf(question) + 4, choices.Max(TextMetrics.WidthOf) + keyWidth + 12));
        var inner = Math.Clamp(Math.Max(widest, MinInner), 10, Screen.LineWidth - 6);
        var questionLines = Wrap(question, inner - 4);
        var noteLines = note.Length == 0 ? [] : Wrap(note, inner - 4);
        // A blank row, the question, the note, a blank row, the choices and a blank row
        var choiceTop = 2 + questionLines.Count + noteLines.Count;
        var (top, left, width, _) = DrawFrame(caption, inner, choiceTop + choices.Count + 1);
        WriteText(top, left, questionLines, noteLines, warning, width);
        for (var number = 0; number < choices.Count; number++)
        {
            Tools.SetCursor(top + 1 + choiceTop + number, left + 1);
            Console.Write("   " + keys[number].PadRight(keyWidth) + " .... " +
                          Tools.TruncateString(choices[number], width - keyWidth - 11, true));
        }

        WriteHints(ChoiceKeys);
        while (true)
        {
            for (var i = 0; i < choices.Count; i++)
            {
                Tools.SetCursor(top + 1 + choiceTop + i, left + 1);
                ConsoleColors.SetColors(i == selected
                    ? ConsoleColors.ColorPreset.SelectorInverted
                    : ConsoleColors.ColorPreset.Normal);
                Console.Write(i == selected ? "->" : "  ");
                ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            }

            Tools.SetCursor(Screen.LineCount - 1, 0);
            var key = Console.ReadKey(true);
            var navigation = Keyboard.ToNavigationKey(key).Key;
            if (key.Key is ConsoleKey.Escape or ConsoleKey.Q || navigation == ConsoleKey.LeftArrow) return -1;
            if (key.Key is ConsoleKey.Enter or ConsoleKey.Spacebar || navigation == ConsoleKey.RightArrow)
            {
                return selected;
            }

            // The selection stops at the first and the last choice
            if (navigation == ConsoleKey.DownArrow) selected = Math.Min(selected + 1, choices.Count - 1);
            if (navigation == ConsoleKey.UpArrow) selected = Math.Max(selected - 1, 0);
            if (key.KeyChar == 'G') selected = choices.Count - 1;
            if (key.KeyChar == 'g' && Console.ReadKey(true).KeyChar == 'g') selected = 0;
            // The key of a choice picks it
            var typed = char.ToUpperInvariant(key.KeyChar).ToString();
            for (var i = 0; i < keys.Count; i++)
            {
                if (keys[i] == typed) return i;
            }
        }
    }

    /// <summary>
    /// Show a message (highlighted) and a note in a box until a key is pressed
    /// </summary>
    internal static void Message(string caption, string message, string note)
    {
        var inner = Math.Clamp(Math.Max(Math.Max(TextMetrics.WidthOf(caption), TextMetrics.WidthOf(message)) + 4, MinInner),
            10, Screen.LineWidth - 6);
        var messageLines = Wrap(message, inner - 4);
        var noteLines = note.Length == 0 ? [] : Wrap(note, inner - 4);
        // A blank row, the message, the note, a blank row, the keys and a blank row
        var keyRow = 2 + messageLines.Count + noteLines.Count;
        var (top, left, _, _) = DrawFrame(caption, inner, keyRow + 2);
        WriteText(top, left, messageLines, noteLines, false, inner);
        Tools.SetCursor(top + 1 + keyRow, left + 3);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
        Console.Write(AnyKey);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        Tools.WriteBottomLine(string.Empty, ConsoleColors.ColorPreset.Normal, false);
        Console.ReadKey(true);
    }

    /// <summary>
    /// Show texts in a box as wide as the screen allows, each wrapped to the width of the box, until a key is
    /// pressed. A text can have its own colors (a heading); an empty text is a blank row. When there is more than the
    /// box can hold, the rest is left out and told about
    /// </summary>
    internal static void ShowTexts(string caption, IReadOnlyList<(string Text, ConsoleColors.ColorPreset Colors)> texts)
    {
        var inner = Screen.LineWidth - 6;
        var rows = new List<(string Text, ConsoleColors.ColorPreset Colors)>();
        foreach (var (text, colors) in texts)
        {
            if (text.Length == 0) rows.Add((string.Empty, colors));
            else rows.AddRange(Wrap(text, inner - 4).Select(line => (line, colors)));
        }

        // A blank row, the texts, a blank row, the key and a blank row
        var most = Math.Max(1, Screen.LineCount - 9);
        if (rows.Count > most)
        {
            rows = rows.GetRange(0, most - 1);
            rows.Add(("(more than fits in the window)", ConsoleColors.ColorPreset.Divider));
        }

        var (top, left, _, _) = DrawFrame(caption, inner, rows.Count + 4);
        for (var i = 0; i < rows.Count; i++)
        {
            Tools.SetCursor(top + 2 + i, left + 3);
            ConsoleColors.SetColors(rows[i].Colors);
            Console.Write(TextMetrics.Printable(rows[i].Text));
        }

        Tools.SetCursor(top + 3 + rows.Count, left + 3);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
        Console.Write(AnyKey);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        Tools.WriteBottomLine(string.Empty, ConsoleColors.ColorPreset.Normal, false);
        Console.ReadKey(true);
    }

    /// <summary>
    /// The top of a box with its title in a tab, as the tabs of Blyant's frames:
    ///
    ///   .---------.
    ///   | Caption '-----------.
    ///   |                     |
    /// </summary>
    private static void DrawTitleTab(int tabRow, int left, int inner, string caption)
    {
        caption = Tools.TruncateString(caption.Trim(), Math.Max(1, inner - 4), true);
        var width = TextMetrics.WidthOf(caption);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.BoxCorner);
        Tools.SetCursor(tabRow, left);
        Console.Write("." + new string('-', width + 2) + ".");
        Tools.SetCursor(tabRow + 1, left);
        Console.Write('|');
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.BoxTitle);
        Console.Write(" " + caption + " ");
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.BoxCorner);
        Console.Write("'" + new string('-', Math.Max(0, inner - width - 3)) + ".");
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    private static void DrawBox(int tabRow, int left, int inner, int height, string caption)
    {
        DrawTitleTab(tabRow, left, inner, caption);
        var top = tabRow + 1;
        for (var row = 0; row < height; row++)
        {
            Tools.SetCursor(top + 1 + row, left);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.BoxCorner);
            Console.Write('|');
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            Console.Write(new string(' ', inner));
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.BoxBorder);
            Console.Write('|');
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.BoxBorder);
        Tools.SetCursor(top + height + 1, left);
        Console.Write("'" + new string('-', inner) + "'");
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    // Draw the frame of a box with its title (in a tab above it) and a blank inside, in the middle of the screen.
    // A box shown before it (the page not drawn again) is covered. Returns where the box is (its top line) and how
    // big it is inside
    private static (int Top, int Left, int Inner, int Height) DrawFrame(string caption, int inner, int height)
    {
        var left = Math.Max(0, (Screen.LineWidth - inner - 2) / 2);
        // The tab of the title takes the row above the box
        var tabRow = Math.Max(1, (Screen.LineCount - height - 3) / 2);
        if (_shown is { } shown)
        {
            var right = Math.Max(left + inner + 1, shown.Right);
            var bottom = Math.Max(tabRow + height + 2, shown.Bottom);
            tabRow = Math.Min(tabRow, shown.Top);
            left = Math.Min(left, shown.Left);
            inner = right - left - 1;
            height = bottom - tabRow - 2;
        }

        var top = tabRow + 1;
        _shown = (tabRow, left, left + inner + 1, top + height + 1);
        Tools.HideCursor();
        DrawBox(tabRow, left, inner, height, caption);
        return (top, left, inner, height);
    }

    // The highlighted lines (a question or a message) and the lines below them, from the second row of a box
    private static void WriteText(int top, int left, List<string> highlighted, List<string> plain, bool warning,
        int inner)
    {
        var row = top + 2;
        foreach (var line in highlighted)
        {
            Tools.SetCursor(row++, left + 2);
            ConsoleColors.SetColors(warning
                ? ConsoleColors.ColorPreset.WarningInverted
                : ConsoleColors.ColorPreset.AttentionInverted);
            Console.Write(" " + Tools.TruncateString(line, inner - 3, true) + " ");
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        foreach (var line in plain)
        {
            Tools.SetCursor(row++, left + 3);
            Console.Write(Tools.TruncateString(line, inner - 3, true));
        }
    }

    internal static void WriteHints(string hints)
    {
        Tools.WriteBottomLine(string.Empty, ConsoleColors.ColorPreset.Normal, false);
        Tools.SetCursor(Screen.LineCount - 1, 0);
        Menu.WriteKeyHints(hints);
    }

    /// <summary>
    /// Break a text into lines no wider than a width, between words (a word that is too long is cut)
    /// </summary>
    internal static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var joined = line.Length == 0 ? word : line + " " + word;
            if (TextMetrics.WidthOf(joined) <= width)
            {
                line = joined;
                continue;
            }

            if (line.Length > 0) lines.Add(line);
            line = TextMetrics.WidthOf(word) <= width ? word : Tools.TruncateString(word, width, true);
        }

        if (line.Length > 0 || lines.Count == 0) lines.Add(line);
        return lines;
    }
}
