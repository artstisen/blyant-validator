namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// A row of the help: a heading, or a key (or command) and what it does. Adapted from Blyant's Shortcut.
/// </summary>
internal sealed record HelpRow(string Key, string Description, bool IsHeading = false)
{
    public static HelpRow Heading(string text) => new(string.Empty, text, true);

    public string Text => Key + " " + Description;

    public void Write()
    {
        if (IsHeading)
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.HeaderColor);
            Console.Write(" " + Description);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            return;
        }

        Console.Write("    ");
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Accent);
        Console.Write(Key);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        Console.Write(" ");
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
        Console.Write(new string('.', Math.Max(1, 20 - Key.Length)));
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        // A long description is cut at the edge of the window
        Console.Write(Tools.FitLine(" " + Description, Console.CursorLeft));
    }
}

/// <summary>
/// The help: the list of keys shown in pages, which can be browsed both ways and searched.
/// Adapted from Blyant's Tools.ShowHelp.
/// </summary>
internal static class HelpViewer
{
    private const string HelpKeys = "| h/l Page | j/k Line | /text Search | n/N Next/Previous | Esc Close";

    public static void Show(IReadOnlyList<HelpRow> lines)
    {
        var top = 0;
        var search = string.Empty;
        var match = -1;
        var message = string.Empty;
        Tools.HideCursor();
        while (true)
        {
            Screen.ApplyAutoSize();
            var pageSize = Screen.LineCount - 1;
            var maxTop = Math.Max(0, lines.Count - pageSize);
            top = Math.Clamp(top, 0, maxTop);
            Tools.ClearScreen();
            for (var row = 0; row < pageSize && top + row < lines.Count; row++)
            {
                var item = lines[top + row];
                Tools.SetCursor(row, 0);
                item.Write();
                // Mark the lines matching the search
                if (search.Length > 0 && item.Text.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    Tools.SetCursor(row, 0);
                    ConsoleColors.SetColors(top + row == match
                        ? ConsoleColors.ColorPreset.HighlightInverted
                        : ConsoleColors.ColorPreset.Divider);
                    Console.Write(">");
                    ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
                }
            }

            // Page number and keys
            var pages = Math.Max(1, (lines.Count + pageSize - 1) / pageSize);
            var page = top >= maxTop ? pages : top / pageSize + 1;
            Tools.SetCursor(Screen.LineCount - 1, 0);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.KeyHint);
            var pageText = $" Page {page} of {pages} ";
            Console.Write(pageText);
            if (message.Length > 0)
            {
                ConsoleColors.SetColors(ConsoleColors.ColorPreset.AttentionInverted);
                Console.Write(Tools.FitLine(" " + message + " ", pageText.Length));
            }
            else
            {
                Console.Write(Tools.FitLine(HelpKeys, pageText.Length));
            }

            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            message = string.Empty;

            // (results coming in for the page below do not change the help)
            ConsoleKeyInfo key;
            do key = Keyboard.ReadKey();
            while (Keyboard.IsWake(key));
            if (Keyboard.IsRedraw(key)) continue;
            if (key.Key is ConsoleKey.Escape or ConsoleKey.Q) break;

            if (key.KeyChar == '/')
            {
                // Search: go to the first matching line from the top of the page
                var text = CommandPrompt.Read('/');
                if (text == null) continue;
                search = text;
                match = Find(lines, search, top - 1, true);
                if (match < 0)
                {
                    message = "Not found: " + search;
                    search = string.Empty;
                }
            }
            else if (key.KeyChar is 'n' or 'N' && search.Length > 0)
            {
                match = Find(lines, search, match, key.KeyChar == 'n');
            }
            else if (key.KeyChar == 'G' || key.Key == ConsoleKey.End)
            {
                top = maxTop;
            }
            else if (key.KeyChar == 'g')
            {
                if (Console.ReadKey(true).KeyChar == 'g') top = 0;
            }
            else if (key.Key == ConsoleKey.Home)
            {
                top = 0;
            }
            else if (key.Key is ConsoleKey.J or ConsoleKey.DownArrow)
            {
                top++;
            }
            else if (key.Key is ConsoleKey.K or ConsoleKey.UpArrow)
            {
                top--;
            }
            else if (key.Key is ConsoleKey.L or ConsoleKey.RightArrow or ConsoleKey.PageDown or ConsoleKey.Spacebar
                     or ConsoleKey.Enter)
            {
                top += pageSize;
            }
            else if (key.Key is ConsoleKey.H or ConsoleKey.LeftArrow or ConsoleKey.PageUp or ConsoleKey.B)
            {
                top -= pageSize;
            }

            // Show the current match, with a few lines above it
            if (match >= 0 && key.KeyChar is '/' or 'n' or 'N') top = match - 2;
        }

        Tools.ClearScreen();
    }

    // The next (or previous) line of the help containing a text, wrapping around. -1 if there is none
    private static int Find(IReadOnlyList<HelpRow> lines, string search, int from, bool forward)
    {
        var count = lines.Count;
        for (var i = 1; i <= count; i++)
        {
            var index = ((from + (forward ? i : -i)) % count + count) % count;
            if (lines[index].Text.Contains(search, StringComparison.OrdinalIgnoreCase)) return index;
        }

        return -1;
    }
}
