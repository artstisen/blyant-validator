using Blyant.Validator.Cli.Tui.Highlighting;
using Blyant.Validator.Core;

namespace Blyant.Validator.Cli.Tui;

/// <summary>
/// A file as the peek shows it: its lines, their colors, and the problems found in it by line. It does not touch the
/// console, so it can be tested.
/// </summary>
internal sealed class PeekDocument
{
    // The most of a file a peek shows: the lines up to this size, with a line at the end telling that there is more
    // (as in Blyant's peek)
    internal const int MaxCharacters = 1_000_000;
    internal const int MaxLines = 20_000;

    /// <summary>The most of a file read for the peek: as many bytes as the characters shown can take.</summary>
    internal const int MaxBytes = MaxCharacters * 4;

    // Longer lines are shown without colors: the highlighters go through a line again for some marks ("[", "<"), which
    // takes too long on a line of a minified file
    internal const int MaxHighlightedLength = 5_000;

    private readonly Dictionary<int, List<Diagnostic>> _byLine = [];
    private readonly int[] _states;
    private readonly SyntaxHighlighter.Kind _kind;

    /// <param name="cut">Only the start of the file was read (it is longer).</param>
    public PeekDocument(string path, string text, IReadOnlyList<Diagnostic> diagnostics, bool cut = false)
    {
        Path = path;
        Diagnostics = [.. diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column)];
        _kind = SyntaxHighlighter.KindOf(path);

        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        // (the line break at the end of the last line does not start another line)
        var count = lines.Length > 1 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var kept = new List<string>();
        var characters = 0;
        IsCut = cut;
        for (var i = 0; i < count; i++)
        {
            var line = lines[i];
            if (kept.Count == MaxLines || characters + line.Length > MaxCharacters)
            {
                // A line longer than what is left (a minified file) is shown in part
                if (kept.Count < MaxLines && characters < MaxCharacters)
                    kept.Add(line[..(MaxCharacters - characters)].Replace('\t', ' '));
                IsCut = true;
                break;
            }
            kept.Add(line.Replace('\t', ' '));
            characters += line.Length + 1;
        }
        Lines = kept;

        foreach (var d in Diagnostics)
        {
            // A problem without a line (vnu's file-level messages) is shown on the first line; one after the part shown
            // is only in the list of problems
            if (d.Start.Line > Lines.Count && IsCut) continue;
            var index = Math.Clamp(d.Start.Line - 1, 0, Math.Max(0, Lines.Count - 1));
            if (!_byLine.TryGetValue(index, out var list)) _byLine[index] = list = [];
            list.Add(d);
        }

        // The state each line starts in, for the colors (computed once: the highlighters go line by line)
        _states = new int[Lines.Count + 1];
        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Length > MaxHighlightedLength)
            {
                _states[i + 1] = _states[i];
                continue;
            }

            SyntaxHighlighter.Highlight(_kind, Lines[i], _states[i], out var next);
            _states[i + 1] = next;
        }
    }

    public string Path { get; }
    public IReadOnlyList<string> Lines { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>True when the file is longer than the peek shows.</summary>
    public bool IsCut { get; }

    /// <summary>The problems on a line (0-based), in the order of their columns.</summary>
    public IReadOnlyList<Diagnostic> On(int line) => _byLine.TryGetValue(line, out var list) ? list : [];

    /// <summary>The most serious problem on a line, or null.</summary>
    public Severity? WorstOn(int line) => _byLine.TryGetValue(line, out var list) ? list.Max(d => d.Severity) : null;

    /// <summary>The colors of a line, or null.</summary>
    public SyntaxStyle[]? StylesOf(int line) =>
        Lines[line].Length > MaxHighlightedLength ? null : SyntaxHighlighter.Highlight(_kind, Lines[line], _states[line], out _);

    /// <summary>The next (or previous) line with a problem after (before) a line, going round; null if there is none.</summary>
    public int? NextProblemLine(int from, bool forward)
    {
        if (_byLine.Count == 0) return null;
        var lines = _byLine.Keys.Order().ToList();
        if (forward) return lines.FirstOrDefault(l => l > from, lines[0]);
        return lines.LastOrDefault(l => l < from, lines[^1]);
    }

    /// <summary>The next (or previous) line containing a text, going round; null if there is none.</summary>
    public int? Find(string text, int from, bool forward)
    {
        var count = Lines.Count;
        for (var i = 1; i <= count; i++)
        {
            var index = ((from + (forward ? i : -i)) % count + count) % count;
            if (Lines[index].Contains(text, StringComparison.OrdinalIgnoreCase)) return index;
        }

        return null;
    }
}
