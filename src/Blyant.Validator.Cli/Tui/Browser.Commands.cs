using Blyant.Validator.Cli.Tui.Terminal;
using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Html;
using Blyant.Validator.Xml;

namespace Blyant.Validator.Cli.Tui;

// The commands of the ":" command line, the help, and formatting files
internal static partial class Browser
{
    private enum CommandResult
    {
        Done,
        Quit,
    }

    /// <summary>
    /// The commands of the browser: the names Tab completes to, the short forms, and what can follow a command.
    /// When a command is added, renamed or removed, change this table, RunCommand and HelpRows together
    /// </summary>
    internal static readonly CommandTable CommandTable = new(
        ["all", "cd", "engine", "format", "fragment", "help", "home", "ignore", "name", "panes", "path", "peek", "problems",
            "quit", "refresh", "unignore"],
        ["q", "h", "exit", "close"],
        new Dictionary<string, string[]> { ["engine"] = ["native", "vnu"] });

    private static CommandResult RunCommand(string command)
    {
        var name = CommandPrompt.Name(command);
        var argument = CommandPrompt.Argument(command);
        switch (name)
        {
            case "q" or "quit" or "exit" or "close":
                return CommandResult.Quit;
            case "h" or "help":
                HelpViewer.Show(HelpRows);
                break;
            case "home":
                ChangeDirectory(HomeDirectory(), null);
                break;
            case "cd":
                // Go to a directory by path (a relative path starts from the directory shown)
                var path = argument;
                if (path.Length > 0 && ActivePane.Directory.Length > 0) path = Path.GetFullPath(path, ActivePane.Directory);
                if (path.Length > 0 && Directory.Exists(path)) ChangeDirectory(Path.GetFullPath(path), null);
                else _message = "Directory not found";
                break;
            case "panes":
                TogglePanes();
                break;
            case "peek":
                TogglePeek(PeekMode.Text);
                break;
            case "problems":
                TogglePeek(PeekMode.Problems);
                break;
            case "format":
                FormatFiles();
                break;
            case "refresh":
                RefreshPane(ActivePane);
                break;
            case "path":
                // The full path of the selected file or directory (on ".." the directory shown) to the clipboard, as
                // ":path" in Blyant's file browser
                var selected = SelectedItem() is { Path.Length: > 0 } item ? item.Path : ActivePane.Directory;
                if (selected.Length > 0) _message = Clipboard.Copy(selected, selected);
                break;
            case "name":
                // The name of the selected file or directory to the clipboard, as ":name" in Blyant's file browser
                var selectedName = SelectedName();
                if (selectedName.Length > 0) _message = Clipboard.Copy(selectedName, selectedName);
                break;
            case "all":
                // All files are listed, or only HTML, CSS and XML
                _showAll = !_showAll;
                foreach (var pane in Panes) Reload(pane);
                _message = _showAll ? "All files are listed" : "Only HTML, CSS, XML and Markdown files are listed";
                break;
            case "engine":
                if (argument.Length == 0)
                {
                    _message = $"Engine: {_results.Engine} (:engine native or :engine vnu)";
                }
                else if (argument.ToLowerInvariant() is "native" or "vnu")
                {
                    _results.Engine = argument.ToLowerInvariant();
                    _message = _results.Engine == "vnu"
                        ? "Engine: vnu (the Nu Html Checker, run with Java for each file)"
                        : "Engine: native";
                }
                else
                {
                    _message = "Use :engine native or :engine vnu";
                }
                break;
            case "fragment":
                ToggleFragment();
                break;
            case "ignore":
                Ignore(argument);
                break;
            case "unignore":
                Unignore(argument);
                break;
            default:
                CommandPrompt.ShowUnknown(command);
                break;
        }

        return CommandResult.Done;
    }

    // The folder whose rules :fragment, :ignore and :unignore change: the one shown in the pane with the focus
    private static string? RulesFolder()
    {
        if (ActivePane.Directory.Length > 0) return ActivePane.Directory;
        _message = "Go to a folder first";
        return null;
    }

    // ":fragment": the HTML files of the folder (and its subfolders) are checked as fragments, or as pages again
    private static void ToggleFragment()
    {
        if (RulesFolder() is not { } folder) return;
        var rules = _results.Rules.ForDirectory(folder);
        _results.SetRules(folder, rules with { Fragment = !rules.Fragment });
        _message = !rules.Fragment
            ? "HTML here is checked as fragments (the content of a <body>)" +
              (_results.Engine == "vnu" ? ", with the native engine only" : "")
            : "HTML here is checked as full pages";
        _peekSource = null;
    }

    // ":ignore code ...": ignore problems with these codes in the folder (and its subfolders). Without a code: the code
    // of the problem selected in the problems of the peek, or else the codes ignored here
    private static void Ignore(string argument)
    {
        if (RulesFolder() is not { } folder) return;
        var rules = _results.Rules.ForDirectory(folder);
        IReadOnlyList<string> codes = IgnoreList.Split(argument);
        if (codes.Count == 0)
        {
            if (SelectedProblem() is not { } problem)
            {
                _message = rules.Ignore.Count == 0
                    ? "Nothing is ignored here (:ignore code, or select a problem with P)"
                    : "Ignored here: " + string.Join(", ", rules.Ignore) + " (:unignore code)";
                return;
            }

            codes = [problem.Code];
        }

        if (codes.FirstOrDefault(c => !IgnoreList.IsValid(c)) is { } wrong)
        {
            _message = $"Not a problem code: {wrong} (as html.missing-lang or css.*)";
            return;
        }

        var added = codes.Where(c => !rules.Ignore.Contains(c)).ToList();
        if (added.Count == 0)
        {
            _message = "Already ignored here: " + string.Join(", ", codes);
            return;
        }

        _results.SetRules(folder, rules with { Ignore = [.. rules.Ignore, .. added] });
        _message = "Ignored here: " + string.Join(", ", added) + " (:unignore to undo)";
        _peekSource = null;
    }

    // ":unignore code ...": stop ignoring codes in the folder; without a code: all of them
    private static void Unignore(string argument)
    {
        if (RulesFolder() is not { } folder) return;
        var rules = _results.Rules.ForDirectory(folder);
        var codes = IgnoreList.Split(argument);
        var removed = codes.Count == 0 ? rules.Ignore.ToList() : rules.Ignore.Where(codes.Contains).ToList();
        if (removed.Count == 0)
        {
            _message = rules.Ignore.Count == 0 ? "Nothing is ignored here"
                : "Not ignored here; ignored are: " + string.Join(", ", rules.Ignore);
            return;
        }

        _results.SetRules(folder, rules with { Ignore = [.. rules.Ignore.Except(removed)] });
        _message = "No longer ignored here: " + string.Join(", ", removed);
        _peekSource = null;
    }

    // The problem selected in the problems of the peek (P), if they are shown
    private static Diagnostic? SelectedProblem() =>
        _peeking && _peekMode == PeekMode.Problems && _peekDocument is { Diagnostics.Count: > 0 } document
            ? document.Diagnostics[Math.Clamp(_problemSelected, 0, document.Diagnostics.Count - 1)]
            : null;

    // F or ":format": format the marked files of the pane, or the selected file, after asking. Only whitespace changes,
    // and a file is replaced safely (DocumentIo.SafeWriteAllText)
    private static void FormatFiles()
    {
        var paths = ActivePane.Marks.Count > 0
            ? ActivePane.Items.Where(i => ActivePane.Marks.Contains(i.Path)).Select(i => i.Path).ToList()
            : SelectedItem() is { IsDirectory: false } item ? [item.Path] : [];
        // (Markdown is validated, but not formatted)
        paths = paths.Where(p => SourceDocument.DetectKind(p) is DocumentKind.Html or DocumentKind.Css or DocumentKind.Xml).ToList();
        if (paths.Count == 0)
        {
            _message = "Select an HTML, CSS or XML file to format";
            return;
        }

        var changes = new List<(string Path, string Text, bool Bom, int Before, int After)>();
        var refused = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                var document = DocumentIo.Load(path);
                IFormatter formatter = document.Kind switch
                {
                    DocumentKind.Css => new CssFormatter(),
                    DocumentKind.Xml => new XmlFormatter(),
                    _ => new HtmlFormatter(),
                };
                var formatted = formatter.Format(document, new FormatOptions());
                if (formatted != document.Content)
                    changes.Add((path, formatted, document.HasUtf8Bom, LineCount(document.Content), LineCount(formatted)));
            }
            catch (Core.FormatException e)
            {
                refused.Add($"{Path.GetFileName(path)}: {e.Message}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                refused.Add($"{Path.GetFileName(path)}: {e.Message}");
            }
        }

        if (changes.Count == 0)
        {
            _message = refused.Count > 0 ? "Not formatted: " + refused[0]
                : paths.Count == 1 ? "Already formatted" : "All are formatted already";
            return;
        }

        var question = changes.Count == 1
            ? $"Format {Path.GetFileName(changes[0].Path)}?"
            : $"Format {changes.Count} files?";
        var note = changes.Count == 1
            ? $"{changes[0].Before} lines become {changes[0].After}. Only layout changes."
            : "Only layout changes.";
        if (refused.Count > 0) note += $" {refused.Count} cannot be formatted (unclosed blocks).";
        if (!ChoiceBox.Confirm("Format", question, note, false)) return;

        var done = 0;
        foreach (var change in changes)
        {
            try
            {
                DocumentIo.SafeWriteAllText(change.Path, change.Text, change.Bom);
                _results.Refresh(change.Path);
                done++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                refused.Add($"{Path.GetFileName(change.Path)}: {e.Message}");
            }
        }

        _peekSource = null;
        _message = done == 1 ? "Formatted " + Path.GetFileName(changes[0].Path) : $"Formatted {done} files";
        if (refused.Count > 0) _message += ". Not formatted: " + refused[0];
    }

    // The name for ":name": of the selected file or directory; on "..", of the directory shown (the root of a drive
    // has no name of its own: "C:\"); in the list of drives, the drive
    private static string SelectedName()
    {
        if (SelectedItem() is { } item && item.Name != ParentName) return item.Name;
        var directory = Path.TrimEndingDirectorySeparator(ActivePane.Directory);
        var name = Path.GetFileName(directory);
        return name.Length > 0 ? name : ActivePane.Directory;
    }

    private static int LineCount(string text) => text.Length == 0 ? 0 : text.Count(c => c == '\n') + (text.EndsWith('\n') ? 0 : 1);

    /// <summary>The help (? or ":help")</summary>
    internal static readonly IReadOnlyList<HelpRow> HelpRows =
    [
        HelpRow.Heading("Blyant Validator: the files"),
        new("j/k Up/Down", "Select a file or directory"),
        new("gg G", "First and last"),
        new("Ctrl+D Ctrl+U", "A page down or up (also PgDn, PgUp)"),
        new("l Enter", "Open a directory; on a file: the peek, with the focus in it"),
        new("h - Backspace", "The directory above"),
        new("Tab", "The other pane (into the peek while peeking)"),
        new("T", "One pane or two"),
        new("V", "Peek on or off: the selected file in the other pane, with its problems marked"),
        new("P", "The problems of the selected file in the other pane (P again: off)"),
        new("F", "Format the selected file, or the marked files, after asking"),
        new("Space", "Mark a file (or unmark it), to format several"),
        new("/text", "Only list the names containing a text"),
        new("r", "Read the directory again, and check its files again"),
        new("Esc", "End the peek, remove the marks or the filter; then quit (after asking)"),
        new("q", "Quit (after asking)"),
        new("Ctrl+C", "Quit at once"),
        new("?", "This help"),
        HelpRow.Heading("The result column"),
        new("ok", "No errors or warnings"),
        new("2E 1W", "Errors and warnings found"),
        new("...", "Being checked"),
        new("failed", "Could not be checked (the peek's problems tell why)"),
        HelpRow.Heading("In the peek (Tab)"),
        new("j/k gg G", "Move the cursor (Ctrl+D/U, PgDn/PgUp too)"),
        new("n N", "The next and previous problem; after /text: the next and previous match"),
        new("] [", "The next and previous problem (n and N go to problems again)"),
        new("h/l 0", "Scroll left and right, back to the start"),
        new("/text", "Search (n and N then go to the matches)"),
        new("I", "The full text of the problems of the line (in the problems: of the selected one)"),
        new("P", "The problems of the file, or its text again"),
        new("Enter", "In the problems: show the problem in the file"),
        new("Tab Esc", "Back to the list"),
        new(":command", "Runs in the list (:ignore ignores the selected problem)"),
        HelpRow.Heading("Commands"),
        new(":cd path", "Go to a directory (relative to the one shown)"),
        new(":home", "The home directory"),
        new(":panes :peek", "As T and V"),
        new(":problems", "As P"),
        new(":format", "As F"),
        new(":engine name", "native (built in) or vnu (the Nu Html Checker, needs Java)"),
        new(":path", "The full path of the selected file or directory to the clipboard (OSC 52)"),
        new(":name", "The name of the selected file or directory to the clipboard"),
        new(":all", "List all files, or only HTML, CSS, XML and Markdown"),
        HelpRow.Heading("Not a full check (for the folder shown and its subfolders)"),
        new(":fragment", "HTML files are fragments (the content of a <body>, for a CMS), or full pages again"),
        new(":ignore code", "Ignore problems with a code (css.* ignores all CSS codes); no code: the selected problem"),
        new(":unignore code", "Stop ignoring a code; no code: all of them"),
        new("FRAGMENT IGNORE", "The badges above a folder that is not fully checked"),
        new(":refresh", "As r"),
        new(":help :quit", "This help; quit (:q)"),
    ];
}
