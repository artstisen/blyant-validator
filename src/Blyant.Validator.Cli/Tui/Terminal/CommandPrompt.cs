namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// The ":" command line on the bottom line of the window (and the "/" of the filters and searches), with Tab
/// completion and Up/Down for the commands given before. Adapted from Blyant's CommandLine.cs.
/// </summary>
internal static class CommandPrompt
{
    private const char Prefix = ':';
    private const string CommandLabel = "COMMAND:"; // Shown in front of a command being typed

    // The columns of the window: the text stays on its line also when the window is narrower than the pages
    private static int VisibleWidth()
    {
        try
        {
            return Math.Min(Screen.LineWidth, Console.WindowWidth);
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        {
            return Screen.LineWidth;
        }
    }

    /// <summary>
    /// Let the user type a text on the bottom line
    /// </summary>
    /// <param name="prefix">":" for a command, "/" for a filter or a search</param>
    /// <param name="commands">For a command: the commands Tab completes to, and where the history is kept</param>
    /// <returns>The text trimmed, or null if canceled or empty</returns>
    internal static string? Read(char prefix, CommandTable? commands = null)
    {
        var command = string.Empty;
        var completion = commands == null ? null : new CommandCompletion(commands);
        var recall = commands == null ? null : new CommandRecall(commands.History);
        Tools.WriteBottomLine(prefix == Prefix ? CommandLabel : prefix.ToString(),
            ConsoleColors.ColorPreset.CommandLineInverted, true);
        ConsoleColors.SetColors(ConsoleColors.ColorPreset.CommandLineInverted);
        Tools.ShowCursor();
        var left = Console.CursorLeft; // Where the text starts

        // Show another text on the line instead of the one typed (counted in columns, and only when it fits)
        void Replace(string text)
        {
            if (text == command || TextMetrics.WidthOf(text) + 2 >= VisibleWidth() - left) return;
            var shown = TextMetrics.WidthOf(command);
            var width = TextMetrics.WidthOf(text);
            Tools.SetCursor(Console.CursorTop, left);
            Console.Write(text + new string(' ', Math.Max(0, shown - width)));
            Tools.SetCursor(Console.CursorTop, left + width);
            command = text;
        }

        try
        {
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Escape) return null;

                if (key.Key == ConsoleKey.Tab)
                {
                    // Complete the command. Tab again: the next command that starts the same (Shift+Tab: the previous)
                    Replace(completion?.Next(command, (key.Modifiers & ConsoleModifiers.Shift) == 0) ?? command);
                    recall?.Reset();
                    continue;
                }

                if (key.Key is ConsoleKey.UpArrow or ConsoleKey.DownArrow)
                {
                    var recalled = key.Key == ConsoleKey.UpArrow ? recall?.Previous(command) : recall?.Next();
                    if (recalled != null)
                    {
                        Replace(recalled);
                        completion?.Reset();
                    }

                    continue;
                }

                if (key.Key == ConsoleKey.Enter)
                {
                    commands?.History.Add(command);
                    command = command.Trim();
                    return command.Length > 0 ? command : null;
                }

                if (key.Key == ConsoleKey.Backspace)
                {
                    if (command.Length == 0) return null;
                    // A whole character (an emoji is two chars, a wide one two columns)
                    var length = TextMetrics.PreviousElementLength(command, command.Length);
                    var cells = TextMetrics.WidthOf(command[^length..]);
                    command = command[..^length];
                    recall?.Reset();
                    Tools.SetCursor(Console.CursorTop, Math.Max(left, Console.CursorLeft - cells));
                    Console.Write(new string(' ', cells));
                    Tools.SetCursor(Console.CursorTop, Math.Max(left, Console.CursorLeft - cells));
                }
                else if (!char.IsControl(key.KeyChar) && key.KeyChar != '\0' &&
                         TextMetrics.WidthOf(command) + 2 < VisibleWidth() - left)
                {
                    command += key.KeyChar;
                    recall?.Reset();
                    Console.Write(key.KeyChar);
                }
            }
        }
        finally
        {
            Tools.HideCursor();
            // Remove the highlighted line
            Tools.WriteBottomLine(string.Empty, ConsoleColors.ColorPreset.Normal, false);
        }
    }

    /// <summary>
    /// Tell the user that a command is unknown and wait for a key press
    /// </summary>
    internal static void ShowUnknown(string command)
    {
        // (" Not a command: ", the command, " " and " [Press any key to continue]" within the line)
        Tools.WriteBottomLine(" Not a command: " + Tools.TruncateString(command, Math.Max(4, Screen.LineWidth - 46), true) + " ",
            ConsoleColors.ColorPreset.CommandErrorInverted, false);
        Console.Write(" [Press any key to continue]");
        Console.ReadKey(true);
    }

    /// <summary>
    /// The first word of a command ("cd" for "cd src"), in lowercase
    /// </summary>
    internal static string Name(string command)
    {
        var space = command.IndexOf(' ');
        return (space < 0 ? command : command[..space]).ToLowerInvariant();
    }

    /// <summary>
    /// The text after the first word of a command ("src" for "cd src")
    /// </summary>
    internal static string Argument(string command)
    {
        var space = command.IndexOf(' ');
        return space < 0 ? string.Empty : command[(space + 1)..].Trim();
    }
}

/// <summary>
/// The commands of a place: the names Tab completes to (Names), the short forms that are accepted but not offered,
/// the words that can follow a command, and the commands given before (History). Adapted from Blyant's Commands.cs.
/// It does not touch the console, so it can be tested.
/// </summary>
internal sealed class CommandTable(IReadOnlyList<string> names, IReadOnlyList<string> shortForms,
    IReadOnlyDictionary<string, string[]> arguments)
{
    public IReadOnlyList<string> Names { get; } = names;
    public IReadOnlyList<string> ShortForms { get; } = shortForms;
    public IReadOnlyDictionary<string, string[]> Arguments { get; } = arguments;
    public CommandHistory History { get; } = new();

    public bool IsKnown(string name) => Names.Contains(name) || ShortForms.Contains(name);
}

/// <summary>
/// The commands given on a command line, the newest first: Up and Down bring them back. Kept in memory only.
/// Adapted from Blyant's CommandHistory.cs.
/// </summary>
internal sealed class CommandHistory
{
    internal const int MaxCount = 20;
    private readonly List<string> _commands = [];

    internal void Add(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return;
        _commands.Remove(command);
        _commands.Insert(0, command);
        if (_commands.Count > MaxCount) _commands.RemoveRange(MaxCount, _commands.Count - MaxCount);
    }

    internal IReadOnlyList<string> All => [.. _commands];
}

/// <summary>
/// Up and Down on one command line: the commands given before, the newest first. When something has been typed, only
/// the commands that start with it are brought back, and going down past the newest gives back what was typed.
/// Copied from Blyant's CommandHistory.cs.
/// </summary>
internal sealed class CommandRecall(CommandHistory history)
{
    private readonly IReadOnlyList<string> _commands = history.All; // As they were when the command line was opened
    private int _index = -1; // The command shown (-1: what was typed)
    private string _typed = string.Empty;

    internal string? Previous(string current)
    {
        if (_index < 0) _typed = current;
        for (var i = _index + 1; i < _commands.Count; i++)
        {
            if (!Matches(_commands[i])) continue;
            _index = i;
            return _commands[i];
        }

        return null;
    }

    internal string? Next()
    {
        if (_index < 0) return null;
        for (var i = _index - 1; i >= 0; i--)
        {
            if (!Matches(_commands[i])) continue;
            _index = i;
            return _commands[i];
        }

        _index = -1;
        return _typed;
    }

    internal void Reset() => _index = -1;

    private bool Matches(string command) => command.StartsWith(_typed, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Completes the command being typed when Tab is pressed. Tab again gives the next command that starts with what was
/// typed, and after the last of them what was typed is shown again. After a command and a space, the words that can
/// follow it are completed (":engine v" gives ":engine vnu"). Adapted from Blyant's Commands.cs.
/// </summary>
internal sealed class CommandCompletion(CommandTable table)
{
    private string _shown = string.Empty; // What the last completion gave: typing something else starts over
    private string _before = string.Empty; // The command and space in front of a word being completed
    private string _typed = string.Empty;
    private List<string> _matches = [];
    private int _index = -1; // The command shown (-1: what was typed)
    private bool _active;

    internal void Reset()
    {
        _active = false;
        _index = -1;
        _matches = [];
    }

    internal string Next(string text, bool forward)
    {
        if (!_active || !string.Equals(text, _shown, StringComparison.Ordinal))
        {
            Reset();
            if (!Start(text)) return text;
            _active = true;
        }

        // What was typed is one of the places gone through
        var count = _matches.Count + 1;
        _index = (_index + 1 + (forward ? 1 : count - 1)) % count - 1;
        _shown = _before + (_index < 0 ? _typed : _matches[_index]);
        return _shown;
    }

    private bool Start(string text)
    {
        var space = text.IndexOf(' ');
        IEnumerable<string> candidates;
        if (space > 0)
        {
            if (!table.Arguments.TryGetValue(text[..space].ToLowerInvariant(), out var words)) return false;
            _before = text[..(space + 1)];
            _typed = text[(space + 1)..];
            if (_typed.Contains(' ')) return false;
            candidates = words;
        }
        else
        {
            _before = string.Empty;
            _typed = text;
            candidates = table.Names;
        }

        _matches = candidates.Where(word => word.StartsWith(_typed, StringComparison.OrdinalIgnoreCase) &&
                                            !string.Equals(word, _typed, StringComparison.OrdinalIgnoreCase)).ToList();
        return _matches.Count > 0;
    }
}
