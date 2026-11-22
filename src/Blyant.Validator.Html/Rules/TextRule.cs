using System.Collections.Frozen;
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
    private static readonly FrozenSet<string> Code = FrozenSet.Create(StringComparer.Ordinal, [ "pre", "code", "kbd", "samp", "var", "listing" ]);

    public void Check(HtmlRuleContext context) => Walk(context.Root, context);

    private static void Walk(HElement element, HtmlRuleContext context)
    {
        for (var i = 0; i < element.Children.Count; i++)
        {
            var node = element.Children[i];
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

                CheckStartTagWithoutBracket(element, i, text, context);

                // "</p>>": a ">" right after an end tag (after a start tag it is text: a breadcrumb <span>></span>)
                if (text.Raw.StartsWith('>') && i > 0 && element.Children[i - 1] is HStrayEndTag or HElement { EndTag: not null } &&
                    context.Document.Content[text.Start - 1] == '>')
                    context.Report(text.Start, Severity.Warning, "html.stray-bracket",
                        "A “>” right after an end tag is shown as text: is it one too many? Write “&gt;” to show it.");
            }
            else if (node is HElement child && !child.IsForeign && !Code.Contains(child.Lower))
            {
                Walk(child, context);
            }
        }
    }

    // "li>Two</li>": a start tag without its "<", told when it starts a line (or the text, right after a tag) and its
    // end tag follows the text (a stray end tag, or the end of the element of that name the text ends). So a CSS
    // selector in the text (ul>li, "the selector li>a") is not
    private static void CheckStartTagWithoutBracket(HElement parent, int index, HText text, HtmlRuleContext context)
    {
        foreach (Match match in StartTagWithoutBracket().Matches(text.Raw))
        {
            var name = match.Groups["name"].Value;
            if (!HtmlSpec.Elements.ContainsKey(name) || IsInAWebAddress(text.Raw, match.Index)) continue;
            var lineStart = text.Raw.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
            if (match.Index > 0 && !string.IsNullOrWhiteSpace(text.Raw[lineStart..match.Index])) continue;
            var closed = index + 1 < parent.Children.Count
                ? parent.Children[index + 1] is HStrayEndTag stray && IsEndTagOf(stray.Raw, name)
                : parent.EndTag is not null && parent.Lower.Equals(name, StringComparison.OrdinalIgnoreCase);
            if (!closed) continue;
            context.Report(text.Start + match.Index, Severity.Warning, "html.text-looks-like-start-tag",
                $"“{name}>” in the text looks like a start tag without its “<”: browsers show it as text. Write “<{name}>”.");
        }
    }

    // "</li>" or "</li >" for li, not "</link>"
    private static bool IsEndTagOf(string endTag, string name) =>
        endTag.StartsWith("</" + name, StringComparison.OrdinalIgnoreCase) && endTag.Length > name.Length + 2 &&
        endTag[name.Length + 2] is '>' or ' ' or '\t' or '\n' or '\r' or '\f' or '/';

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

    // "name>" at the start of a word (not after a letter, digit, "<" or "/")
    [GeneratedRegex(@"(?<![\p{L}\p{N}</])(?<name>[A-Za-z][A-Za-z0-9]*)>")]
    private static partial Regex StartTagWithoutBracket();
}
