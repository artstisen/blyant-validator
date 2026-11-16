using System.Text.RegularExpressions;
using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// Text that looks like an end tag without its "&lt;": "fast fragt&lt;/a&gt;/li&gt;", "&lt;li&gt;Normal/li&gt;". It is
/// valid HTML (an end tag such as &lt;/li&gt; may be left out, and "&gt;" may stand in text), so browsers show "/li&gt;"
/// on the page, but it is almost always a "&lt;" left out by mistake: a warning. Only the names of HTML elements count;
/// a name of one letter (a, b, p …) not right after a letter or digit (a/b&gt;); not in a web address
/// (https://example.com/p&gt;), and not in code (&lt;pre&gt;, &lt;code&gt; ...) or in SVG and MathML. Written
/// "/li&amp;gt;", the text is meant, and is not told.
/// </summary>
public sealed partial class TextRule : IHtmlRule
{
    // Elements whose text is code, where "/name>" may be meant (the text of xmp and plaintext is raw text, not
    // read as text nodes; listing is read as pre is)
    private static readonly HashSet<string> Code = new(StringComparer.Ordinal) { "pre", "code", "kbd", "samp", "var", "listing" };

    public void Check(HtmlRuleContext context) => Walk(context.Root, context);

    private static void Walk(HElement element, HtmlRuleContext context)
    {
        foreach (var node in element.Children)
        {
            if (node is HText text)
            {
                foreach (Match match in EndTagWithoutBracket().Matches(text.Raw))
                {
                    var name = match.Groups["name"].Value;
                    if (!HtmlSpec.Elements.ContainsKey(name) || IsPartOfAWord(text.Raw, match.Index, name) ||
                        IsInAWebAddress(text.Raw, match.Index))
                        continue;
                    context.Report(text.Start + match.Index, Severity.Warning, "html.text-looks-like-end-tag",
                        $"“/{name}>” in the text looks like an end tag without its “<”: browsers show it as text. " +
                        $"Write “</{name}>” for the end tag, or “/{name}&gt;” to show it.");
                }
            }
            else if (node is HElement child && !child.IsForeign && !Code.Contains(child.Lower))
            {
                Walk(child, context);
            }
        }
    }

    // A name of one letter right after a letter or digit is most likely a word with a slash (a/b>, 1/p>); a longer
    // name is the end tag ("Normal/li>")
    private static bool IsPartOfAWord(string text, int slash, string name) =>
        name.Length == 1 && slash > 0 && char.IsLetterOrDigit(text[slash - 1]);

    // The "/" is in a web address: the text before it, back to a blank, has "://" or starts with "www."
    private static bool IsInAWebAddress(string text, int slash)
    {
        var start = slash;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1])) start--;
        var word = text.AsSpan(start, slash - start);
        return word.Contains("://", StringComparison.Ordinal) || word.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
    }

    // "/name>" not right after "<" (that is an end tag)
    [GeneratedRegex(@"(?<!<)/(?<name>[A-Za-z][A-Za-z0-9]*)>")]
    private static partial Regex EndTagWithoutBracket();
}
