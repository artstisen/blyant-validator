using Blyant.Validator.Cli.Tui.Highlighting;
using Blyant.Validator.Cli.Tui.Terminal;
using Blyant.Validator.Core;

namespace Blyant.Validator.Cli.Tui;

// The peek: the selected file in the other pane, read-only, with its problems marked (V), or the list of its problems
// (P). It follows the selection; Tab goes into it to move and search. Adapted from the peek of Blyant's file browser
// (FileExplorer.Manage.cs), with a small viewer of its own instead of Blyant's editor
internal static partial class Browser
{
    private enum PeekMode
    {
        Text,
        Problems,
    }

    private const string PeekTextKeys = "j/k Line | n/N Problem | I Full text | / Search | P Problems | Tab Back";
    private const string PeekSearchKeys = "j/k Line | n/N Match | ]/[ Problem | I Full text | P Problems | Tab Back";
    private const string PeekProblemKeys = "j/k Select | Enter Show in file | I Full text | P Text | Tab Back";

    private static bool _peeking;
    private static bool _peekWasDual;
    private static bool _peekFocused;
    private static PeekMode _peekMode;

    // The file the peek shows, as it was read, and what is in view
    private sealed record PeekSource(string Path, DateTime Written, long Length, FileResult? Result);

    private static PeekSource? _peekSource;
    private static PeekDocument? _peekDocument;
    private static string? _peekNote; // Shown instead of a file (a directory, not a text file, cannot be read)
    private static string? _peekShownPath; // The path the view below belongs to
    private static int _peekTop; // The first line shown
    private static int _peekCursor; // The line of the cursor (when the focus is in the peek)
    private static int _peekLeft; // The first column shown
    private static int _problemSelected;
    private static int _problemTop;
    private static string _peekSearch = string.Empty;
    private static string? _peekCommand; // A ":" command typed in the peek, run when the focus is back in the list
    private static bool _quitAtOnce; // Ctrl+C in the peek: the program ends, without asking

    // n and N go to the next search match (after "/") or the next problem (when the peek is entered, and after ] or [)
    private static bool _nextFindsSearch;

    // V (text) or P (problems): the peek on, in that mode; the same key again ends it, the other key switches
    private static void TogglePeek(PeekMode mode)
    {
        if (_peeking && _peekMode == mode)
        {
            StopPeek();
            return;
        }

        _peekMode = mode;
        if (_peeking) return;
        _peekWasDual = _dualPane;
        _dualPane = true;
        _peeking = true;
        _peekShownPath = null;
        _peekSource = null;
    }

    // The other pane shows its list again
    private static void StopPeek()
    {
        if (!_peeking) return;
        _peeking = false;
        _peekShownPath = null;
        _peekSource = null;
        _dualPane = _peekWasDual;
        Reload(OtherPane);
    }

    // The keys that keep the peek: moving in the list and going into directories (the peek follows), Tab (into the
    // peek), V, P and Esc, and what does not use the other pane
    private static bool KeepsPeek(ConsoleKeyInfo key) =>
        key.Key is ConsoleKey.J or ConsoleKey.K or ConsoleKey.DownArrow or ConsoleKey.UpArrow or ConsoleKey.PageDown
            or ConsoleKey.PageUp or ConsoleKey.Home or ConsoleKey.End or ConsoleKey.H or ConsoleKey.L
            or ConsoleKey.LeftArrow or ConsoleKey.RightArrow or ConsoleKey.Enter or ConsoleKey.Backspace or ConsoleKey.Tab
            or ConsoleKey.Escape or ConsoleKey.V or ConsoleKey.P or ConsoleKey.Spacebar or ConsoleKey.R or ConsoleKey.F ||
        key.KeyChar is 'g' or 'G' or '-' or ':' or '/' or '?' ||
        Keyboard.IsControl(key, ConsoleKey.D) || Keyboard.IsControl(key, ConsoleKey.F) ||
        Keyboard.IsControl(key, ConsoleKey.U) || Keyboard.IsControl(key, ConsoleKey.B);

    // The title of the pane of the peek: the name of the file and its result
    private static string PeekTitle()
    {
        if (SelectedItem() is not { IsDirectory: false } item) return PeekedDirectory() ?? string.Empty;
        if (!ResultStore.IsValidated(item.Path)) return item.Name + " - not checked";
        var result = _results.Last(item.Path);
        var summary = result == null || _results.IsPending(item.Path) ? "checking..."
            : result.Failure != null ? "could not be checked"
            : (result.Errors, result.Warnings) switch
            {
                (0, 0) => "no problems",
                (_, 0) => Plural(result.Errors, "error"),
                (0, _) => Plural(result.Warnings, "warning"),
                _ => Plural(result.Errors, "error") + ", " + Plural(result.Warnings, "warning"),
            };
        // Not a full check: said with the result
        if (result is { Failure: null } && (result.Fragment || result.Ignored > 0))
        {
            var notes = new List<string>();
            if (result.Fragment) notes.Add("fragment");
            if (result.Ignored > 0) notes.Add(result.Ignored + " ignored");
            summary += " (" + string.Join(", ", notes) + ")";
        }

        return item.Name + " - " + summary;
    }

    private static string Plural(int count, string word) => count + " " + word + (count == 1 ? "" : "s");

    // The directory the peek shows: the selected directory, or on ".." the directory above
    private static string? PeekedDirectory()
    {
        if (ActivePane.Directory.Length == 0 || SelectedItem() is not { } item) return null;
        if (item.Name == ParentName) return System.IO.Directory.GetParent(ActivePane.Directory)?.FullName;
        return item.IsDirectory ? item.Path : null;
    }

    // True when the file of the peek, or its result, is not the one shown (it was changed, or checked again)
    private static bool PeekChanged()
    {
        if (SelectedItem() is not { IsDirectory: false } item) return false;
        var info = new FileInfo(item.Path);
        if (!info.Exists) return _peekSource != null;
        var result = _results.Get(item.Path);
        return _peekSource is not { } source || source.Path != item.Path || source.Written != info.LastWriteTimeUtc ||
               source.Length != info.Length || !ReferenceEquals(source.Result, result);
    }

    // Read the selected file for the peek (again, if it or its result changed)
    private static void LoadPeek()
    {
        if (SelectedItem() is not { IsDirectory: false } item)
        {
            _peekSource = null;
            _peekDocument = null;
            _peekNote = PeekedDirectory() is { } directory ? null : "Select a file to peek at";
            return;
        }

        FileInfo info;
        try
        {
            info = new FileInfo(item.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _peekNote = e.Message;
            _peekDocument = null;
            return;
        }

        var result = _results.Get(item.Path);
        var source = new PeekSource(item.Path, info.LastWriteTimeUtc, info.Length, result);
        if (source == _peekSource) return;
        _peekSource = source;
        _peekNote = null;
        try
        {
            // (only the start of a large file is read: the peek shows at most PeekDocument.MaxCharacters of it)
            var text = DocumentIo.LoadStart(item.Path, PeekDocument.MaxBytes, out var cut).Content;
            if (text.Contains('\0'))
            {
                _peekNote = "Not a text file";
                _peekDocument = null;
                return;
            }

            _peekDocument = new PeekDocument(item.Path, text, result?.Diagnostics ?? [], cut);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _peekNote = e.Message;
            _peekDocument = null;
        }
    }

    // The peek of the selected file, in the other pane (when the selection has changed, or all is drawn again)
    private static void DrawPeek(bool all)
    {
        if (!_peeking || _peekFocused) return;
        var path = SelectedItem()?.Path ?? string.Empty;
        if (!all && path == _peekShownPath) return;
        try
        {
            // While keys are waiting (j held down), the peek is drawn when they have been handled
            if (Console.KeyAvailable)
            {
                _peekShownPath = null;
                return;
            }
        }
        catch (InvalidOperationException)
        {
            // Keys cannot be asked about: drawn now
        }

        LoadPeek();
        if (path != _peekShownPath)
        {
            // A new file: shown from a few lines above its first problem
            _peekShownPath = path;
            var first = _peekDocument?.NextProblemLine(-1, true) ?? 0;
            _peekTop = first < PageSize - 2 ? 0 : first - 3;
            _peekCursor = first;
            _peekLeft = 0;
            _problemSelected = 0;
            _problemTop = 0;
        }

        DrawPaneHeader(1 - _active);
        DrawPeekBody(false);
    }

    private static void DrawPeekBody(bool focused)
    {
        var (left, width) = PaneArea(1 - _active);
        if (PeekedDirectory() is { } directory && SelectedItem() is { IsDirectory: true })
        {
            DrawPeekLines(left, width, DirectoryContents(directory, PageSize), false);
        }
        else if (_peekDocument == null)
        {
            DrawPeekLines(left, width, [_peekNote ?? string.Empty], true);
        }
        else if (_peekMode == PeekMode.Problems)
        {
            DrawProblems(left, width, focused);
        }
        else
        {
            DrawText(left, width, focused);
        }
    }

    // The names in a directory, as a pane would show them: directories first (ending in "/"), then the files
    private static List<string> DirectoryContents(string directory, int count)
    {
        try
        {
            var info = new DirectoryInfo(directory);
            var names = info.EnumerateDirectories().Where(d => IsShown(d, string.Empty))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).Select(d => d.Name + "/").ToList();
            names.AddRange(info.EnumerateFiles()
                .Where(f => IsShown(f, string.Empty) && (_showAll || ResultStore.IsValidated(f.Name)))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).Select(f => f.Name));
            if (names.Count == 0) return ["(no files to check)"];
            if (names.Count <= count) return names;
            var shown = names.Take(count).ToList();
            shown[^1] = $"... and {names.Count - count + 1} more";
            return shown;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [e.Message];
        }
    }

    // Lines in the pane of the peek, from its first row (the rest of the pane is cleared): a note is dimmed
    private static void DrawPeekLines(int left, int width, IReadOnlyList<string> lines, bool dimmed)
    {
        for (var row = 0; row < PageSize; row++)
        {
            Tools.SetCursor(ListTop + row, left);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            Console.Write(new string(' ', width));
            if (row >= lines.Count) continue;
            Tools.SetCursor(ListTop + row, left + 1);
            if (dimmed) ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
            Console.Write(Tools.TruncateString(lines[row], width - 2, true));
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    // The text of the file: a mark for the most serious problem of a line, the line numbers, and the text in its
    // colors, with the place of each error and warning highlighted
    private static void DrawText(int left, int width, bool focused)
    {
        var document = _peekDocument!;
        var numberWidth = Math.Max(3, document.Lines.Count.ToString().Length);
        var textLeft = left + numberWidth + 2;
        var textWidth = Math.Max(1, left + width - textLeft);
        for (var row = 0; row < PageSize; row++)
        {
            var line = _peekTop + row;
            Tools.SetCursor(ListTop + row, left);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            Console.Write(new string(' ', width));
            Tools.SetCursor(ListTop + row, left);
            if (line >= document.Lines.Count)
            {
                if (line == document.Lines.Count && document.IsCut)
                {
                    ConsoleColors.SetColors(ConsoleColors.ColorPreset.Divider);
                    Console.Write(Tools.TruncateString(" (the rest of the file is not shown)", width, true));
                }
                continue;
            }

            switch (document.WorstOn(line))
            {
                case Severity.Error:
                    ConsoleColors.SetColors(ConsoleColors.ColorPreset.Error);
                    Console.Write('E');
                    break;
                case Severity.Warning:
                    ConsoleColors.SetColors(ConsoleColors.ColorPreset.Warning);
                    Console.Write('W');
                    break;
                case Severity.Info:
                    ConsoleColors.SetColors(ConsoleColors.ColorPreset.Info);
                    Console.Write('i');
                    break;
                default:
                    Console.Write(' ');
                    break;
            }

            var isCursor = focused && line == _peekCursor;
            ConsoleColors.SetColors(isCursor ? ConsoleColors.ColorPreset.SelectorInverted
                : ConsoleColors.ColorPreset.LineNumber);
            Console.Write((line + 1).ToString().PadLeft(numberWidth));
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            Console.Write(' ');
            WriteLine(document, line, textWidth);
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    // A line of the file from the first column shown, in its colors
    private static void WriteLine(PeekDocument document, int line, int width)
    {
        var text = document.Lines[line];
        var start = Math.Min(_peekLeft, text.Length);
        var visible = TextMetrics.StartPart(text[start..], width);
        if (visible.Length == 0) return;
        var styles = document.StylesOf(line);
        // The places of the errors and warnings of the line (columns count from 1)
        var marks = document.On(line)
            .Where(d => d.Severity >= Severity.Warning && d.Start.Column > 0)
            .GroupBy(d => d.Start.Column - 1)
            .ToDictionary(g => g.Key, g => g.Max(d => d.Severity));

        var runStart = 0;
        for (var i = 1; i <= visible.Length; i++)
        {
            if (i < visible.Length && LookOf(start + i) == LookOf(start + runStart)) continue;
            SetLook(LookOf(start + runStart));
            Console.Write(TextMetrics.Printable(visible[runStart..i]));
            runStart = i;
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
        return;

        // How a character is shown: a problem at it (1: warning, 2: error), else its color (10 + style)
        int LookOf(int index)
        {
            if (marks.TryGetValue(index, out var severity)) return severity == Severity.Error ? 2 : 1;
            return 10 + (int)(styles is { } s && index < s.Length ? s[index] : SyntaxStyle.None);
        }

        static void SetLook(int look)
        {
            switch (look)
            {
                case 2:
                    ConsoleColors.SetColors(ConsoleColors.ColorPreset.WarningInverted);
                    break;
                case 1:
                    ConsoleColors.SetColors(ConsoleColors.ColorPreset.AttentionInverted);
                    break;
                case 10:
                    ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
                    break;
                default:
                    ConsoleColors.SetSyntaxColor(SyntaxHighlighter.ColorOf((SyntaxStyle)(look - 10)));
                    break;
            }
        }
    }

    // The problems of the file: "12:5  E  message", the selected one highlighted when the focus is in the peek
    private static void DrawProblems(int left, int width, bool focused)
    {
        var document = _peekDocument!;
        var result = _peekSource?.Result;
        IReadOnlyList<string> notes = !ResultStore.IsValidated(document.Path)
                ? ["Not checked: not HTML, CSS, XML or Markdown"]
            : result == null ? ["Checking..."]
            : result.Failure != null ? ["Could not be checked: " + result.Failure]
            : document.Diagnostics.Count == 0 ? ["No problems found."]
            : [];
        if (notes.Count > 0)
        {
            DrawPeekLines(left, width, notes, result == null);
            return;
        }

        _problemSelected = Math.Clamp(_problemSelected, 0, document.Diagnostics.Count - 1);
        if (_problemSelected < _problemTop) _problemTop = _problemSelected;
        if (_problemSelected >= _problemTop + PageSize) _problemTop = _problemSelected - PageSize + 1;
        for (var row = 0; row < PageSize; row++)
        {
            var index = _problemTop + row;
            Tools.SetCursor(ListTop + row, left);
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            Console.Write(new string(' ', width));
            if (index >= document.Diagnostics.Count) continue;
            var d = document.Diagnostics[index];
            Tools.SetCursor(ListTop + row, left);
            var selected = focused && index == _problemSelected;
            ConsoleColors.SetColors(selected ? ConsoleColors.ColorPreset.SelectorInverted : ConsoleColors.ColorPreset.LineNumber);
            var place = d.Start.Line > 0 ? $"{d.Start.Line}:{d.Start.Column}" : "-";
            Console.Write(" " + place.PadRight(8));
            ConsoleColors.SetColors(d.Severity switch
            {
                Severity.Error => ConsoleColors.ColorPreset.Error,
                Severity.Warning => ConsoleColors.ColorPreset.Warning,
                _ => ConsoleColors.ColorPreset.Info,
            });
            Console.Write(d.Severity switch { Severity.Error => "E ", Severity.Warning => "W ", _ => "i " });
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            // (a place longer than 8, as "123:123456" in a minified file, takes room from the message)
            Console.Write(Tools.TruncateString(d.Message, Math.Max(1, width - 4 - Math.Max(8, place.Length)), true));
        }

        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    // Tab while peeking: the focus goes into the peek, to move and search in it. Tab or Esc goes back to the list. The
    // peek of a directory is not gone into: the directory is opened in the other pane instead
    private static void FocusPeek()
    {
        if (PeekedDirectory() is { } directory && SelectedItem() is { IsDirectory: true })
        {
            OpenPeekedDirectory(directory);
            return;
        }

        LoadPeek();
        if (_peekDocument == null)
        {
            _message = _peekNote ?? "Select a file to peek at";
            return;
        }

        _peekFocused = true;
        _nextFindsSearch = false;
        try
        {
            DrawPaneHeader(0);
            DrawPaneHeader(1);
            PeekLoop();
        }
        finally
        {
            _peekFocused = false;
            _peekShownPath = SelectedItem()?.Path;
        }
    }

    private static void PeekLoop()
    {
        var message = string.Empty;
        while (true)
        {
            var document = _peekDocument!;
            KeepPeekCursorInView(document);
            DrawPaneHeader(1 - _active);
            DrawPeekBody(true);
            DrawPeekBottomLine(document, message);
            message = string.Empty;
            if (Tools.TakeRedrawRequest())
            {
                Draw(true);
                continue;
            }

            var key = Keyboard.ReadKey();
            if (Keyboard.IsRedraw(key))
            {
                Screen.ApplyAutoSize();
                RedrawInPeek();
                continue;
            }

            if (Keyboard.IsWake(key))
            {
                // The file was changed, or checked again: shown as it is now, at the same place (and the results of
                // the list beside it too)
                LoadPeek();
                if (_peekDocument == null) return;
                // (the list may have been read again: files gone)
                KeepInView(ActivePane);
                DrawRows(_active);
                DrawStatus();
                continue;
            }

            if (Keyboard.IsControl(key, ConsoleKey.C))
            {
                // Quit at once, as in the list
                _quitAtOnce = true;
                return;
            }

            if (key.Key is ConsoleKey.Tab or ConsoleKey.Escape or ConsoleKey.Q)
                return;

            if (key.Key == ConsoleKey.P)
            {
                SwitchPeekMode(document);
                continue;
            }

            if (key.KeyChar == ':')
            {
                // A command (as :ignore for the selected problem): run from the list
                _peekCommand = CommandPrompt.Read(':', CommandTable);
                // ":ignore" without a code is for the problem selected here (the list of problems), or the first one
                // on the line of the cursor: told now, as the problems may be checked again before it runs
                if (_peekCommand != null && CommandPrompt.Name(_peekCommand) == "ignore" && CommandPrompt.Argument(_peekCommand).Length == 0)
                {
                    var problem = _peekMode == PeekMode.Problems
                        ? document.Diagnostics.ElementAtOrDefault(_problemSelected)
                        : document.On(_peekCursor).FirstOrDefault();
                    if (problem != null) _peekCommand = "ignore " + problem.Code;
                }

                if (_peekCommand != null) return;
                RedrawInPeek();
                continue;
            }

            if (key.Key == ConsoleKey.I)
            {
                message = ShowProblemText(document);
                if (message.Length == 0) RedrawInPeek();
                continue;
            }

            if (_peekMode == PeekMode.Problems) message = ProblemKey(document, key);
            else message = TextKey(document, key);
        }
    }

    // All is drawn again while the focus stays in the peek (the window was resized, or a box covered the panes)
    private static void RedrawInPeek()
    {
        _peekFocused = false;
        Draw(true);
        _peekFocused = true;
        DrawPaneHeader(0);
        DrawPaneHeader(1);
    }

    // I: the full text of the problems of the line of the cursor (in the problems: of the selected one) in a box,
    // for messages longer than the bottom line. Returns a message when there is nothing to show
    private static string ShowProblemText(PeekDocument document)
    {
        IReadOnlyList<Diagnostic> problems = _peekMode == PeekMode.Problems
            ? [.. document.Diagnostics.Skip(_problemSelected).Take(1)]
            : document.On(_peekCursor);
        if (problems.Count == 0)
            return _peekMode == PeekMode.Problems ? "No problems in this file" : "No problem on this line";

        var texts = new List<(string, ConsoleColors.ColorPreset)>();
        foreach (var d in problems)
        {
            if (texts.Count > 0) texts.Add((string.Empty, ConsoleColors.ColorPreset.Normal));
            var place = d.Start.Line > 0 ? $"Line {d.Start.Line}, column {d.Start.Column}" : "The whole file";
            texts.Add(($"{place}: {d.Severity.ToString().ToLowerInvariant()} [{d.Code}]", d.Severity switch
            {
                Severity.Error => ConsoleColors.ColorPreset.Error,
                Severity.Warning => ConsoleColors.ColorPreset.Warning,
                _ => ConsoleColors.ColorPreset.Info,
            }));
            texts.Add((d.Message, ConsoleColors.ColorPreset.Normal));
        }

        var name = Path.GetFileName(document.Path);
        ChoiceBox.ShowTexts(problems.Count == 1 ? $"Problem - {name}" : $"{problems.Count} problems - {name}", texts);
        return string.Empty;
    }

    private static void SwitchPeekMode(PeekDocument document)
    {
        if (_peekMode == PeekMode.Text)
        {
            // The problem at the cursor, or the next one after it
            var index = document.Diagnostics.ToList().FindIndex(d => d.Start.Line - 1 >= _peekCursor);
            _problemSelected = index < 0 ? Math.Max(0, document.Diagnostics.Count - 1) : index;
            _peekMode = PeekMode.Problems;
        }
        else
        {
            ShowProblemInText(document);
        }

        DrawPaneHeader(1 - _active);
    }

    // The text with the cursor on the line of the selected problem, its column in view
    private static void ShowProblemInText(PeekDocument document)
    {
        _peekMode = PeekMode.Text;
        if (document.Diagnostics.Count == 0) return;
        var d = document.Diagnostics[Math.Clamp(_problemSelected, 0, document.Diagnostics.Count - 1)];
        // (a problem after the part of a large file shown: the last line shown)
        _peekCursor = Math.Clamp(d.Start.Line - 1, 0, Math.Max(0, document.Lines.Count - 1));
        _peekTop = Math.Max(0, _peekCursor - PageSize / 3);
        var (_, width) = PaneArea(1 - _active);
        var room = width - 6;
        _peekLeft = d.Start.Column > room ? d.Start.Column - room / 2 : 0;
    }

    private static string ProblemKey(PeekDocument document, ConsoleKeyInfo key)
    {
        var count = document.Diagnostics.Count;
        if (key.Key is ConsoleKey.J or ConsoleKey.DownArrow) _problemSelected++;
        else if (key.Key is ConsoleKey.K or ConsoleKey.UpArrow) _problemSelected--;
        else if (key.Key == ConsoleKey.PageDown || Keyboard.IsControl(key, ConsoleKey.D)) _problemSelected += PageSize;
        else if (key.Key == ConsoleKey.PageUp || Keyboard.IsControl(key, ConsoleKey.U)) _problemSelected -= PageSize;
        else if (key.KeyChar == 'G' || key.Key == ConsoleKey.End) _problemSelected = count - 1;
        else if (key.Key == ConsoleKey.Home || (key.KeyChar == 'g' && Console.ReadKey(true).KeyChar == 'g')) _problemSelected = 0;
        else if (key.Key is ConsoleKey.Enter or ConsoleKey.L or ConsoleKey.RightArrow)
        {
            ShowProblemInText(document);
            DrawPaneHeader(1 - _active);
        }

        _problemSelected = Math.Clamp(_problemSelected, 0, Math.Max(0, count - 1));
        return string.Empty;
    }

    private static string TextKey(PeekDocument document, ConsoleKeyInfo key)
    {
        var last = Math.Max(0, document.Lines.Count - 1);
        if (key.Key is ConsoleKey.J or ConsoleKey.DownArrow) _peekCursor++;
        else if (key.Key is ConsoleKey.K or ConsoleKey.UpArrow) _peekCursor--;
        else if (Keyboard.IsControl(key, ConsoleKey.D)) MovePage(PageSize / 2);
        else if (Keyboard.IsControl(key, ConsoleKey.U)) MovePage(-PageSize / 2);
        else if (key.Key is ConsoleKey.PageDown or ConsoleKey.Spacebar || Keyboard.IsControl(key, ConsoleKey.F)) MovePage(PageSize);
        else if (key.Key == ConsoleKey.PageUp || Keyboard.IsControl(key, ConsoleKey.B)) MovePage(-PageSize);
        else if (key.KeyChar == 'G') _peekCursor = last;
        else if (key.KeyChar == 'g' && Console.ReadKey(true).KeyChar == 'g') _peekCursor = 0;
        else if (key.Key is ConsoleKey.L or ConsoleKey.RightArrow) _peekLeft += 8;
        else if (key.Key is ConsoleKey.H or ConsoleKey.LeftArrow) _peekLeft = Math.Max(0, _peekLeft - 8);
        else if (key.KeyChar == '0' || key.Key == ConsoleKey.Home) _peekLeft = 0;
        else if (key.KeyChar is ']' or '[')
        {
            // n and N go to the problems again
            _nextFindsSearch = false;
            return GoToProblem(document, key.KeyChar == ']');
        }
        else if (key.KeyChar == '/')
        {
            var text = CommandPrompt.Read('/');
            if (text == null) return string.Empty;
            _peekSearch = text;
            _nextFindsSearch = true;
            if (document.Find(text, _peekCursor, true) is { } line) _peekCursor = line;
            else return "Not found: " + text;
        }
        else if (key.KeyChar is 'n' or 'N')
        {
            // As in Vim: what was looked for last, the text searched for or the problems
            if (!_nextFindsSearch) return GoToProblem(document, key.KeyChar == 'n');
            if (document.Find(_peekSearch, _peekCursor, key.KeyChar == 'n') is { } line) _peekCursor = line;
            else return "Not found: " + _peekSearch;
        }

        _peekCursor = Math.Clamp(_peekCursor, 0, last);
        return string.Empty;
    }

    // The cursor to the next (or previous) line with a problem, going round
    private static string GoToProblem(PeekDocument document, bool forward)
    {
        if (document.NextProblemLine(_peekCursor, forward) is not { } line) return "No problems in this file";
        _peekCursor = line;
        return string.Empty;
    }

    private static void MovePage(int rows)
    {
        _peekCursor += rows;
        _peekTop += rows;
    }

    private static void KeepPeekCursorInView(PeekDocument document)
    {
        var last = Math.Max(0, document.Lines.Count - 1);
        _peekCursor = Math.Clamp(_peekCursor, 0, last);
        if (_peekCursor < _peekTop) _peekTop = _peekCursor;
        if (_peekCursor >= _peekTop + PageSize) _peekTop = _peekCursor - PageSize + 1;
        _peekTop = Math.Clamp(_peekTop, 0, Math.Max(0, document.Lines.Count + (document.IsCut ? 1 : 0) - PageSize));
        _peekTop = Math.Min(_peekTop, _peekCursor);
    }

    // The bottom line in the peek: a message, the problem of the line of the cursor (or the one selected), or the keys
    private static void DrawPeekBottomLine(PeekDocument document, string message)
    {
        Tools.WriteBottomLine(string.Empty, ConsoleColors.ColorPreset.Normal, false);
        Tools.SetCursor(Screen.LineCount - 1, 0);
        if (message.Length > 0)
        {
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.AttentionInverted);
            Console.Write(" " + Tools.TruncateString(message, Screen.LineWidth - 4, true) + " ");
            ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
            return;
        }

        var problems = _peekMode == PeekMode.Problems
            ? document.Diagnostics.Skip(_problemSelected).Take(1).ToList()
            : [.. document.On(_peekCursor)];
        if (problems.Count == 0)
        {
            Menu.WriteKeyHints(_peekMode == PeekMode.Problems ? PeekProblemKeys
                : _nextFindsSearch ? PeekSearchKeys : PeekTextKeys);
            return;
        }

        var d = problems[0];
        var more = problems.Count > 1 ? $" (+{problems.Count - 1} more)" : string.Empty;
        ConsoleColors.SetColors(d.Severity == Severity.Error ? ConsoleColors.ColorPreset.WarningInverted
            : d.Severity == Severity.Warning ? ConsoleColors.ColorPreset.AttentionInverted
            : ConsoleColors.ColorPreset.HighlightInverted);
        var text = $" {d.Start.Line}:{d.Start.Column} {d.Severity.ToString().ToLowerInvariant()}: {d.Message}{more} ";
        // A message that does not fit tells that I shows all of it
        const string hint = "... (I: all) ";
        var room = Screen.LineWidth - 2;
        // (messages can quote the file: never written without TextMetrics.Printable)
        Console.Write(TextMetrics.WidthOf(text) <= room
            ? TextMetrics.Printable(text)
            : Tools.TruncateString(text, room - hint.Length, false) + hint);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.Normal);
    }

    // The directory of the peek in the other pane, with its first item selected and the focus there. The peek ends,
    // and two panes stay shown
    private static void OpenPeekedDirectory(string directory)
    {
        var pane = OtherPane;
        var (previous, filter) = (pane.Directory, pane.Filter);
        pane.Directory = directory;
        pane.Filter = string.Empty;
        if (!LoadItems(pane, null))
        {
            (pane.Directory, pane.Filter) = (previous, filter);
            var message = _message;
            LoadItems(pane, null);
            _message = message;
            return;
        }

        if (pane.Items.Count > 1 && pane.Items[0].Name == ParentName) pane.Index = 1;
        _peeking = false;
        _peekShownPath = null;
        _dualPane = true;
        _active = 1 - _active;
    }
}
