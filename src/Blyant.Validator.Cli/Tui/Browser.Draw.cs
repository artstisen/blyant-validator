using System.Globalization;
using Blyant.Validator.Cli.Tui.Terminal;

namespace Blyant.Validator.Cli.Tui;

// Drawing the browser. Adapted from the drawing region of Blyant's FileExplorer.cs
internal static partial class Browser
{
    private const string Caption = "Files";
    private const string BrowserKeys = "j/k Select | V Peek | P Problems | F Format | / Filter | ? Help | q Quit";
    private const string BrowserDualKeys = "Tab Pane | V Peek | P Problems | F Format | / Filter | ? Help | q Quit";
    private const string PeekingKeys = "j/k Next file | Tab Into peek | P Problems/Text | V Esc End peek";
    private const string MarkedKeys = "Space Mark | F Format marked | Esc Unmark | ? Help";

    private const int ResultColumnWidth = 9; // "99E 99W" and a space
    private const int SizeColumnWidth = 9; // "1023.9 KB"
    private const int MinWidthForSize = 50; // The size column is left out in a pane that is narrower

    private static int _shownActive = -1; // The pane that had the focus when the browser was drawn last
    private static bool _shownDual;

    // The columns of a pane: where it starts and how wide it is. Two panes share the window with a separator between
    private static (int Left, int Width) PaneArea(int number)
    {
        if (!IsDual) return (0, Screen.LineWidth - 1);
        var half = (Screen.LineWidth - 1) / 2;
        return number == 0 ? (0, half) : (half + 1, Screen.LineWidth - half - 2);
    }

    /// <summary>
    /// Draw the browser
    /// </summary>
    /// <param name="all">False if only the selection has moved, or the focus has gone to the other pane: then only
    /// the selection pointers, the pane labels and the status are drawn (all is drawn if the list has to scroll)</param>
    private static void Draw(bool all)
    {
        var shown = IsDual ? Panes : [ActivePane];
        foreach (var pane in shown) KeepInView(pane);

        Tools.HideCursor();
        // (the pane of the peek has no rows of its own: it does not count)
        if (!all && _shownDual == IsDual && shown.Where((_, number) => !IsPeekPane(number)).All(pane => pane.Top == pane.ShownTop))
        {
            if (IsDual && _shownActive != _active)
            {
                for (var number = 0; number < Panes.Length; number++) DrawPaneHeader(number);
            }

            for (var number = 0; number < shown.Length; number++)
            {
                if (IsPeekPane(number)) continue;
                var pane = shown[number];
                DrawPointer(number, pane.ShownIndex, false);
                DrawPointer(number, pane.Index, true);
                pane.ShownIndex = pane.Index;
            }

            _shownActive = _active;
            DrawStatus();
            DrawPeek(false);
            return;
        }

        Tools.ClearScreen();
        Menu.WriteModeLabel(Caption);
        Menu.WriteNameInTopBar();
        if (!_results.Engines.IsNative)
        {
            var marker = " ENGINE: " + _results.Engines.ToString().ToUpperInvariant() + " ";
            Tools.SetCursor(0, Math.Max(0, Screen.LineWidth - marker.Length - 1));
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.AttentionInverted);
            Console.Write(marker);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        }

        for (var number = 0; number < shown.Length; number++)
        {
            DrawPaneHeader(number);
            DrawRows(number);
        }

        if (IsDual)
        {
            // The line between the panes
            var column = PaneArea(1).Left - 1;
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
            Tools.SetCursor(1, column);
            Console.Write('|');
            for (var row = 0; row < PageSize; row++)
            {
                Tools.SetCursor(ListTop + row, column);
                Console.Write('|');
            }
        }

        _shownActive = _active;
        _shownDual = IsDual;
        DrawStatus();
        DrawPeek(true);
    }

    /// <summary>
    /// Draw the lists, the status and the peek again without clearing the screen (results came in)
    /// </summary>
    private static void DrawContent()
    {
        var count = IsDual ? Panes.Length : 1;
        for (var number = 0; number < count; number++)
        {
            if (IsPeekPane(number)) continue;
            KeepInView(ShownPane(number));
            if (ShownPane(number).Top != ShownPane(number).ShownTop)
            {
                Draw(true);
                return;
            }
            DrawRows(number);
        }

        DrawStatus();
        DrawPeek(true);
    }

    private static void KeepInView(Pane pane)
    {
        pane.Index = Math.Clamp(pane.Index, 0, Math.Max(0, pane.Items.Count - 1));
        if (pane.Index < pane.Top) pane.Top = pane.Index;
        if (pane.Index >= pane.Top + PageSize) pane.Top = pane.Index - PageSize + 1;
        pane.Top = Math.Clamp(pane.Top, 0, Math.Max(0, pane.Items.Count - 1));
    }

    // The rows of the list of a pane (not on the pane of the peek)
    private static void DrawRows(int number)
    {
        if (IsPeekPane(number)) return;
        var pane = ShownPane(number);
        var (left, width) = PaneArea(number);
        for (var row = 0; row < PageSize; row++)
        {
            if (pane.Top + row < pane.Items.Count)
            {
                DrawItem(number, pane, pane.Top + row);
            }
            else
            {
                Tools.SetCursor(ListTop + row, left);
                ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
                Console.Write(new string(' ', width));
            }
        }

        pane.ShownTop = pane.Top;
        pane.ShownIndex = pane.Index;
    }

    // The pane the peek is shown on, instead of its list (the one without the focus)
    private static bool IsPeekPane(int number) => _peeking && IsDual && number != _active;

    // The pane that is drawn as a number (a single pane is always the first)
    private static Pane ShownPane(int number) => IsDual ? Panes[number] : ActivePane;

    private static bool HasFocus(int number) => !IsDual || number == _active;

    // The two lines above the list of a pane: the directory, and a divider that tells which pane has the focus
    private static void DrawPaneHeader(int number)
    {
        var pane = ShownPane(number);
        var (left, width) = PaneArea(number);
        var peek = IsPeekPane(number);
        var focused = peek ? _peekFocused : HasFocus(number) && !_peekFocused;
        var room = width - 2;
        var title = peek ? PeekTitle() : pane.Directory.Length == 0 ? "Drives" : pane.Directory;
        // The badges of a folder that is not fully checked, at the end of the line (left out if the title would
        // get too short)
        var badge = peek || pane.Directory.Length == 0 ? string.Empty : Badge(_results.Rules.ForDirectory(pane.Directory));
        if (badge.Length > 0 && room - badge.Length - 1 < 12) badge = string.Empty;
        if (badge.Length > 0) room -= badge.Length + 1;
        title = TextMetrics.KeepEnd(title, room);
        Tools.SetCursor(1, left);
        ConsoleColors.SetColors(focused ? ConsoleColors.ColorPreset.HeaderColor : ConsoleColors.ColorPreset.Divider);
        Console.Write(" " + title + new string(' ', Math.Max(0, room - TextMetrics.WidthOf(title))));
        if (badge.Length > 0)
        {
            Console.Write(' ');
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.AttentionInverted);
            Console.Write(badge);
        }
        Tools.SetCursor(2, left);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
        Console.Write(new string('-', IsDual && number == 0 ? width : Math.Min(width, Screen.LineWidth - left)));
        if (!IsDual) return;
        if (number == 0) Console.Write('+');
        Tools.SetCursor(2, left + 2);
        var label = peek ? (_peekMode == PeekMode.Problems ? "PROBLEMS" : "PEEK") : $"PANE {number + 1}";
        if (focused)
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.HighlightInverted);
            Console.Write(" " + label + " ");
        }
        else
        {
            Console.Write(" " + label.ToLowerInvariant() + " ");
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    // The badges of rules that make a check not a full one: " FRAGMENT ", " IGNORE 2 " (two codes are ignored)
    internal static string Badge(CheckRules rules) =>
        (rules.Fragment ? " FRAGMENT " : string.Empty) + (rules.Ignore.Count > 0 ? $" IGNORE {rules.Ignore.Count} " : string.Empty);

    // A row of the list of a pane: the name, the result of the validation, and the size (if the pane is wide enough)
    private static void DrawItem(int number, Pane pane, int index)
    {
        var item = pane.Items[index];
        var row = index - pane.Top;
        var (left, width) = PaneArea(number);
        Tools.SetCursor(ListTop + row, left);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        Console.Write(new string(' ', width));
        DrawPointer(number, index, index == pane.Index);

        var showSize = width >= MinWidthForSize;
        var end = left + width - 1;
        var sizeColumn = end - SizeColumnWidth + 1;
        var resultColumn = (showSize ? sizeColumn - 1 : end + 1) - ResultColumnWidth;
        var name = Tools.TruncateString(item.Name + (item.IsDirectory && item.Name != ParentName ? "/" : ""),
            Math.Max(4, resultColumn - (left + 3) - 1 - ListNumbers.Width(FileNumbers, pane.Items.Count)), true);

        if (item.Path.Length > 0 && pane.Marks.Contains(item.Path))
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.AttentionInverted);
            Console.Write("+");
        }
        else
        {
            Console.Write(" ");
        }

        ConsoleColors.SetColors(item.IsDirectory ? ConsoleColors.ColorPreset.HeaderColor : ConsoleColors.ColorPreset.Normal);
        Console.Write(name);

        if (!item.IsDirectory)
        {
            Tools.SetCursor(ListTop + row, resultColumn);
            WriteResult(item.Path);
        }

        if (showSize)
        {
            var size = item.IsDirectory ? (item.Name == ParentName ? string.Empty : "<DIR>") : FormatSize(item.Size);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
            Tools.SetCursor(ListTop + row, end - size.Length + 1);
            Console.Write(size);
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    // The result of a file in the result column: "ok", "2E 1W", "..." while it is checked, "failed"
    private static void WriteResult(string path)
    {
        if (!ResultStore.IsValidated(path)) return;
        var result = _results.Get(path);
        if (result == null)
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
            Console.Write("...");
            return;
        }

        if (result.Failure != null)
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Error);
            Console.Write("failed");
            return;
        }

        if (result.Errors == 0 && result.Warnings == 0)
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Ok);
            Console.Write("ok");
            return;
        }

        if (result.Errors > 0)
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Error);
            Console.Write(Math.Min(result.Errors, 99) + "E");
        }

        if (result.Warnings > 0)
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Warning);
            Console.Write((result.Errors > 0 ? " " : "") + Math.Min(result.Warnings, 99) + "W");
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => bytes + " B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024.0 * 1024)).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
    };

    // Draw (or remove) the selection pointer in front of an item of a pane, leaving the cursor after it (and before
    // it the number of the row, when the numbers are on). In the pane without the focus the pointer is not
    // highlighted, nor its number
    private static void DrawPointer(int number, int index, bool selected)
    {
        var pane = ShownPane(number);
        var row = index - pane.Top;
        if (row < 0 || row >= PageSize || index >= pane.Items.Count) return;
        Tools.SetCursor(ListTop + row, PaneArea(number).Left);
        ListNumbers.Write(index, ListNumbers.Width(FileNumbers, pane.Items.Count), selected && HasFocus(number));
        ConsoleColors.SetColors(!selected ? ConsoleColors.ColorPreset.Normal :
            HasFocus(number) ? ConsoleColors.ColorPreset.SelectorInverted : ConsoleColors.ColorPreset.Divider);
        Console.Write(selected ? "->" : "  ");
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    // The two bottom lines: the totals and the selected item of each pane, and a message or the keys
    private static void DrawStatus()
    {
        var count = IsDual ? Panes.Length : 1;
        for (var number = 0; number < count; number++)
        {
            var pane = ShownPane(number);
            var (left, width) = PaneArea(number);
            Tools.SetCursor(Screen.LineCount - 2, left);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
            Console.Write(new string('-', IsDual && number == 0 ? width : Math.Min(width, Screen.LineWidth - left)));
            if (IsDual && number == 0) Console.Write('+');
            if (IsPeekPane(number)) continue;

            var status = " " + (pane.Items.Count == 0 ? 0 : pane.Index + 1) + "/" + pane.Items.Count + " ";
            if (pane.Filter.Length > 0) status += "/" + pane.Filter + " ";
            if (pane.Marks.Count > 0) status = " Marked: " + pane.Marks.Count + " -" + status;
            // A count being typed (5j), below the pane it moves in
            if (pane == ActivePane && _listCount.Shown.Length > 0) status = " " + _listCount.Shown + status;
            status = Tools.TruncateString(status, Math.Max(4, width - 6), true);
            // (in cells: a filter can hold wide characters)
            var statusWidth = TextMetrics.WidthOf(status);
            Tools.SetCursor(Screen.LineCount - 2, left + width - statusWidth - 2);
            Console.Write(status);

            // How many files of the pane have errors and warnings
            var (errors, warnings) = Totals(pane);
            var totals = (errors, warnings) switch
            {
                (0, 0) => string.Empty,
                (_, 0) => $" {errors} with errors ",
                (0, _) => $" {warnings} with warnings ",
                _ => $" {errors} with errors, {warnings} with warnings ",
            };
            if (totals.Length > 0 && totals.Length + statusWidth + 6 < width)
            {
                Tools.SetCursor(Screen.LineCount - 2, left + 2);
                ConsoleColors.SetColors(errors > 0 ? ConsoleColors.ColorPreset.Error : ConsoleColors.ColorPreset.Warning);
                Console.Write(totals);
            }
        }

        Tools.SetCursor(Screen.LineCount - 1, 0);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        Console.Write(new string(' ', Screen.LineWidth - 1));
        Tools.SetCursor(Screen.LineCount - 1, 0);
        if (_message.Length > 0)
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.AttentionInverted);
            Console.Write(" " + Tools.TruncateString(_message, Screen.LineWidth - 4, true) + " ");
        }
        else
        {
            Menu.WriteKeyHints(ActivePane.Marks.Count > 0 ? MarkedKeys
                : _peeking ? PeekingKeys
                : IsDual ? BrowserDualKeys : BrowserKeys);
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    // The files of a pane with errors, and with warnings only (of those checked)
    private static (int Errors, int Warnings) Totals(Pane pane)
    {
        var errors = 0;
        var warnings = 0;
        foreach (var item in pane.Items)
        {
            if (item.IsDirectory || _results.Last(item.Path) is not { } result) continue;
            if (result.Errors > 0 || result.Failure != null) errors++;
            else if (result.Warnings > 0) warnings++;
        }

        return (errors, warnings);
    }
}
