using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// The CSS in an HTML file: &lt;style&gt; elements are validated as style sheets, and <c>style</c> attributes as
/// declarations, with the CSS validator. The places of the problems are those in the HTML file.
/// </summary>
public sealed class EmbeddedCssRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var element in context.Root.Descendants())
        {
            if (element.Lower == "style" && element.RawContent is { } css && IsCss(element))
                Add(context, CssValidator.Validate(context.Document.Path, css), element.ContentStart);

            if (element.GetAttribute("style") is { RawValue: not null } style && style.ValueContentStart >= 0 &&
                style.Value.Trim().Length > 0)
                Add(context, CssValidator.ValidateDeclarations(context.Document.Path, style.Value), style.ValueContentStart);
        }
    }

    // A style element is CSS unless its type says otherwise
    private static bool IsCss(HElement element) =>
        element.GetAttribute("type") is not { } type ||
        type.Value.Trim().Length == 0 || type.Value.Trim().Equals("text/css", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Moves the problems of CSS found at an offset of the HTML file to their places in the file: the first line of
    /// the CSS starts at the column of that offset, and its other lines at the start of their lines
    /// </summary>
    private static void Add(HtmlRuleContext context, IReadOnlyList<Diagnostic> found, int offset)
    {
        var at = context.PositionOf(offset);
        SourcePosition Move(SourcePosition p) =>
            p.Line <= 0 ? at : p.Line == 1 ? new(at.Line, at.Column + p.Column - 1) : new(at.Line + p.Line - 1, p.Column);

        foreach (var d in found)
            context.Add(d with { Start = Move(d.Start), End = Move(d.End) });
    }
}
