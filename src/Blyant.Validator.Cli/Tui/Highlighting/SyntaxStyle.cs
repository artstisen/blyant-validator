namespace Blyant.Validator.Cli.Tui.Highlighting;

/// <summary>
/// What a character of a colored text is part of. Adapted from Blyant (Editor/MarkdownHighlighter.cs), keeping the
/// styles the HTML, CSS and JavaScript highlighters use.
/// </summary>
internal enum SyntaxStyle
{
    None,
    Comment,
    String, // Text in quotes, and the values of attributes
    Keyword, // The words of a language, the tags of HTML, the selectors of CSS
    Name, // The names of attributes and properties
    Number, // Numbers, also with units, colors and character entities
}

/// <summary>
/// The coloring of a file in the peek: HTML (with its CSS and JavaScript) and CSS. Adapted from Blyant's
/// SyntaxHighlighter.cs and TextArea.SyntaxColorOf.
/// </summary>
internal static class SyntaxHighlighter
{
    public enum Kind
    {
        None,
        Html,
        Css,
    }

    public static Kind KindOf(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".html" or ".htm" or ".xhtml" => Kind.Html,
            ".css" => Kind.Css,
            _ => Kind.None,
        };

    /// <summary>
    /// The styles of a line, or null if no character has a style
    /// </summary>
    /// <param name="state">The state the line starts in (0 for the first line)</param>
    /// <param name="nextState">The state after the line</param>
    public static SyntaxStyle[]? Highlight(Kind kind, string line, int state, out int nextState)
    {
        nextState = state;
        if (kind == Kind.None || line.Length == 0) return null;
        var styles = new SyntaxStyle[line.Length];
        nextState = kind == Kind.Html
            ? HtmlHighlighter.Scan(line, state, styles)
            : CssHighlighter.Scan(line, 0, line.Length, state, styles);
        return Array.Exists(styles, style => style != SyntaxStyle.None) ? styles : null;
    }

    /// <summary>
    /// The number of the color of a style (Syntax 1 to 12 of the color scheme, counted from 0), as in Blyant's editor
    /// </summary>
    public static int ColorOf(SyntaxStyle style) => style switch
    {
        SyntaxStyle.Keyword => 0,
        SyntaxStyle.Name => 1,
        SyntaxStyle.String => 2,
        SyntaxStyle.Number => 3,
        _ => 5,
    };
}
