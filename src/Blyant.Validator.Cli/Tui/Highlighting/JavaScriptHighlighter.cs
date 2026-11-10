// Copied from Blyant (Editor/JavaScriptHighlighter.cs), MIT License, same author.
namespace Blyant.Validator.Cli.Tui.Highlighting;

/// <summary>
/// The coloring of JavaScript (in HTML's script elements), read by the reader of the languages written in the style
/// of C (CStyleHighlighter.JavaScript), and what the highlighters share (Fill, StringEnd). Blyant's JSON is left out.
/// </summary>
internal static class JavaScriptHighlighter
{
    /// <summary>
    /// Find the styles of a part of a line of JavaScript
    /// </summary>
    /// <param name="line">The line</param>
    /// <param name="start">The first character to look at</param>
    /// <param name="end">The character after the last</param>
    /// <param name="state">The state the part starts in</param>
    /// <param name="styles">The styles of the line, to fill in</param>
    /// <returns>The state after the part</returns>
    internal static int Scan(string line, int start, int end, int state, SyntaxStyle[] styles) =>
        CStyleHighlighter.Scan(CStyleHighlighter.JavaScript, line, start, end, state, styles);

    // Where a string ends: after the closing quote, or at the end (a backslash keeps the next character)
    internal static int StringEnd(string line, int from, int end, char quote, out bool closed)
    {
        closed = false;
        var i = from;
        while (i < end)
        {
            if (line[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (line[i] == quote)
            {
                closed = true;
                return i + 1;
            }

            i++;
        }

        return end;
    }

    internal static void Fill(SyntaxStyle[] styles, int from, int to, SyntaxStyle style)
    {
        for (var i = from; i < to && i < styles.Length; i++) styles[i] = style;
    }
}
