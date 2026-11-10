using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// The CSS in an HTML file: &lt;style&gt; elements are validated as style sheets, and <c>style</c> attributes as
/// declarations, with the CSS validator. The places of the problems are those in the HTML file. Character references
/// in a style attribute (<c>&amp;quot;</c>) are what the browser reads as CSS, so the CSS is checked with them
/// decoded; in SVG, a style element can hold its CSS in a CDATA section.
/// </summary>
public sealed class EmbeddedCssRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var element in context.Root.Descendants())
        {
            if (element.Lower == "style" && element.RawContent is { } css && IsCss(element))
            {
                // (in SVG a CDATA section is markup, not CSS: its delimiters are blanked, so the places stay the same)
                if (element.IsForeign) css = css.Replace("<![CDATA[", "         ", StringComparison.Ordinal).Replace("]]>", "   ", StringComparison.Ordinal);
                Add(context, CssValidator.Validate(context.Document.Path, css), element.ContentStart);
            }

            if (element.GetAttribute("style") is { RawValue: not null } style && style.ValueContentStart >= 0 &&
                style.Value.Trim().Length > 0)
            {
                if (style.Value.Contains('&'))
                {
                    var (decoded, map) = DecodeReferences(style.Value);
                    AddMapped(context, CssValidator.ValidateDeclarations(context.Document.Path, decoded), decoded, map,
                        style.ValueContentStart);
                }
                else
                {
                    Add(context, CssValidator.ValidateDeclarations(context.Document.Path, style.Value), style.ValueContentStart);
                }
            }
        }
    }

    /// <summary>
    /// The text with its character references decoded (the common named ones and numeric ones; others are kept as
    /// written), and for every character of it the offset it came from in the text (one more at the end)
    /// </summary>
    internal static (string Text, int[] Map) DecodeReferences(string raw)
    {
        var text = new System.Text.StringBuilder(raw.Length);
        var map = new List<int>(raw.Length + 1);
        for (var i = 0; i < raw.Length;)
        {
            if (raw[i] == '&' && Reference(raw, i) is { } reference)
            {
                foreach (var c in reference.Value)
                {
                    text.Append(c);
                    map.Add(i);
                }

                i += reference.Length;
                continue;
            }

            text.Append(raw[i]);
            map.Add(i);
            i++;
        }

        map.Add(raw.Length);
        return (text.ToString(), [.. map]);
    }

    // The reference at an "&": what it stands for and how long it is, or null
    private static (string Value, int Length)? Reference(string raw, int at)
    {
        var end = raw.IndexOf(';', at);
        if (end < 0 || end - at > 10) return null;
        var name = raw[(at + 1)..end];
        string? value = name switch
        {
            "quot" => "\"",
            "apos" => "'",
            "amp" => "&",
            "lt" => "<",
            "gt" => ">",
            "nbsp" => "\u00A0",
            ['#', 'x' or 'X', .. var hex] when int.TryParse(hex, System.Globalization.NumberStyles.AllowHexSpecifier,
                System.Globalization.CultureInfo.InvariantCulture, out var code) && IsScalar(code) => char.ConvertFromUtf32(code),
            ['#', .. var digits] when digits.All(char.IsAsciiDigit) && int.TryParse(digits, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var code) && IsScalar(code) => char.ConvertFromUtf32(code),
            _ => null,
        };
        return value is null ? null : (value, end - at + 1);
    }

    private static bool IsScalar(int code) => code is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF);

    // Problems found in decoded text, moved to their places in the file through the map of the decoding
    private static void AddMapped(HtmlRuleContext context, IReadOnlyList<Diagnostic> found, string decoded, int[] map, int offset)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < decoded.Length; i++)
        {
            if (decoded[i] == '\n' || decoded[i] == '\r' && (i + 1 >= decoded.Length || decoded[i + 1] != '\n'))
                lineStarts.Add(i + 1);
        }

        SourcePosition Move(SourcePosition p)
        {
            if (p.Line <= 0) return context.PositionOf(offset);
            var at = lineStarts[Math.Min(p.Line, lineStarts.Count) - 1] + p.Column - 1;
            return context.PositionOf(offset + map[Math.Clamp(at, 0, map.Length - 1)]);
        }

        foreach (var d in found)
            context.Add(d with { Start = Move(d.Start), End = Move(d.End) });
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
