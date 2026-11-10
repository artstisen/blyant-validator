namespace Blyant.Validator.Cli.Tui.Highlighting;

/// <summary>
/// What a character of a colored text is part of. Adapted from Blyant (Editor/MarkdownHighlighter.cs), keeping the
/// styles the HTML, CSS and JavaScript highlighters use.
/// </summary>
internal enum SyntaxStyle
{
    None,
    // Markdown
    Heading1,
    Heading2,
    Heading3,
    Heading4,
    Heading5,
    Heading6,
    Bold,
    Italic,
    Code, // `code` in a line
    CodeBlock, // A line of a block of code (between lines starting with ```)
    Quote,
    ListMarker,
    Link, // [text]
    LinkUrl, // (address), <address> and plain web addresses
    Rule, // A line like "---"
    // Code
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
        Xml,
        Markdown,
    }

    public static Kind KindOf(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".html" or ".htm" or ".xhtml" => Kind.Html,
            ".css" => Kind.Css,
            ".xml" or ".svg" or ".rss" or ".atom" => Kind.Xml,
            ".md" or ".markdown" => Kind.Markdown,
            _ when Path.GetFileName(fileName).ToLowerInvariant() is "llms.txt" or "llms-full.txt" => Kind.Markdown,
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
        if (kind == Kind.Markdown)
        {
            // The state tells if the line is inside a block of code (and the fence that opened it), as in Blyant
            nextState = MarkdownHighlighter.NextFenceState(line, state);
            return MarkdownHighlighter.Highlight(line, state != 0);
        }

        if (kind == Kind.None || line.Length == 0) return null;
        var styles = new SyntaxStyle[line.Length];
        nextState = kind switch
        {
            Kind.Html => HtmlHighlighter.Scan(line, state, styles),
            Kind.Xml => HtmlHighlighter.Scan(line, state, styles, xml: true),
            _ => CssHighlighter.Scan(line, 0, line.Length, state, styles),
        };
        return Array.Exists(styles, style => style != SyntaxStyle.None) ? styles : null;
    }

    /// <summary>
    /// The number of the color of a style (Syntax 1 to 12 of the color scheme, counted from 0), as in Blyant's editor
    /// </summary>
    public static int ColorOf(SyntaxStyle style) => style switch
    {
        SyntaxStyle.Keyword or SyntaxStyle.Bold or SyntaxStyle.ListMarker => 0,
        SyntaxStyle.Name or SyntaxStyle.Italic => 1,
        SyntaxStyle.String or SyntaxStyle.Code or SyntaxStyle.CodeBlock => 2,
        SyntaxStyle.Number => 3,
        SyntaxStyle.Link => 4,
        SyntaxStyle.Heading1 => 6,
        SyntaxStyle.Heading2 => 7,
        SyntaxStyle.Heading3 => 8,
        SyntaxStyle.Heading4 => 9,
        SyntaxStyle.Heading5 => 10,
        SyntaxStyle.Heading6 => 11,
        // Comments, quotes, web addresses and rules
        _ => 5,
    };
}
