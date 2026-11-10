// Copied from Blyant (Editor/CStyleHighlighter.cs), MIT License, same author. Trimmed to JavaScript, the only
// language of the C style in the files the validator shows (in HTML's <script>): Blyant's C#, C++, Rust and PHP are
// left out.
namespace Blyant.Validator.Cli.Tui.Highlighting;

/// <summary>
/// What a language written in the style of C is colored by: its keywords, and the kinds of strings it has besides
/// those of C (// and /* */ comments, strings in ' and ", numbers)
/// </summary>
internal sealed class CStyleLanguage
{
    internal HashSet<string> Keywords { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// `Template strings`, also over several lines (JavaScript)
    /// </summary>
    internal bool TemplateStrings { get; init; }

    /// <summary>
    /// "$" is a part of names (JavaScript)
    /// </summary>
    internal bool DollarInNames { get; init; }
}

/// <summary>
/// Finds the parts of a line of a language written in the style of C to color: comments, strings, keywords and
/// numbers. It is a simple reading, not a parser: the code inside a string (as in `${x}`) is colored as the string,
/// and names are only colored when they are keywords. A line starts in a state, which tells what it is inside of that
/// began on an earlier line (a comment, a string)
/// </summary>
internal static class CStyleHighlighter
{
    // The state a line starts in: inside something that began on an earlier line
    internal const int Normal = 0;
    internal const int InComment = 1; // A /* comment */
    internal const int InTemplate = 2; // A `template string` (JavaScript)

    internal static readonly CStyleLanguage JavaScript = new()
    {
        Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete",
            "do", "else", "export", "extends", "finally", "for", "from", "function", "get", "if", "import", "in",
            "instanceof", "let", "new", "of", "return", "set", "static", "super", "switch", "this", "throw", "try",
            "typeof", "var", "void", "while", "with", "yield",
            "true", "false", "null", "undefined", "NaN", "Infinity"
        },
        TemplateStrings = true,
        DollarInNames = true
    };

    /// <summary>
    /// Find the styles of a part of a line
    /// </summary>
    /// <param name="language">The language</param>
    /// <param name="line">The line</param>
    /// <param name="start">The first character to look at</param>
    /// <param name="end">The character after the last</param>
    /// <param name="state">The state the part starts in</param>
    /// <param name="styles">The styles of the line, to fill in</param>
    /// <returns>The state after the part</returns>
    internal static int Scan(CStyleLanguage language, string line, int start, int end, int state, SyntaxStyle[] styles)
    {
        var i = start;
        while (i < end)
        {
            if (state != Normal)
            {
                // Inside a comment or a template string that began before: up to its end, or the end of the part
                var (to, closed) = state == InComment
                    ? EndOf(line, i, end, "*/")
                    : (JavaScriptHighlighter.StringEnd(line, i, end, '`', out var template), template);
                Fill(styles, i, to, state == InComment ? SyntaxStyle.Comment : SyntaxStyle.String);
                if (closed) state = Normal;
                i = to;
                continue;
            }

            var c = line[i];
            var next = i + 1 < end ? line[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                // To the end of the line
                Fill(styles, i, end, SyntaxStyle.Comment);
                return state;
            }

            if (c == '/' && next == '*')
            {
                Fill(styles, i, i + 2, SyntaxStyle.Comment);
                state = InComment;
                i += 2;
            }
            else if (c is '"' or '\'')
            {
                // A string ends at its quote, or at the end of the line
                var to = JavaScriptHighlighter.StringEnd(line, i + 1, end, c, out _);
                Fill(styles, i, to, SyntaxStyle.String);
                i = to;
            }
            else if (language.TemplateStrings && c == '`')
            {
                styles[i] = SyntaxStyle.String;
                state = InTemplate;
                i++;
            }
            else if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(next)))
            {
                var to = i + 1;
                while (to < end && (char.IsLetterOrDigit(line[to]) || line[to] is '.' or '_')) to++;
                Fill(styles, i, to, SyntaxStyle.Number);
                i = to;
            }
            else if (IsNameStart(language, c))
            {
                var to = NameEnd(language, line, i + 1, end);
                // (a word after a dot is the name of a member, as in "object.default")
                var member = i > start && line[i - 1] == '.';
                if (!member && language.Keywords.Contains(line[i..to])) Fill(styles, i, to, SyntaxStyle.Keyword);
                i = to;
            }
            else
            {
                i++;
            }
        }

        return state;
    }

    private static bool IsNameStart(CStyleLanguage language, char c) =>
        char.IsLetter(c) || c == '_' || (c == '$' && language.DollarInNames);

    private static int NameEnd(CStyleLanguage language, string line, int from, int end)
    {
        while (from < end && (IsNameStart(language, line[from]) || char.IsAsciiDigit(line[from]))) from++;
        return from;
    }

    // Where a comment ends: after its closing mark, or at the end
    private static (int Stop, bool Closed) EndOf(string line, int from, int end, string mark)
    {
        var close = line.IndexOf(mark, from, end - from, StringComparison.Ordinal);
        return close < 0 ? (end, false) : (close + mark.Length, true);
    }

    private static void Fill(SyntaxStyle[] styles, int from, int to, SyntaxStyle style) =>
        JavaScriptHighlighter.Fill(styles, from, to, style);
}
