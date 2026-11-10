using System.Text;
using System.Xml;
using Blyant.Validator.Core;

namespace Blyant.Validator.Xml;

/// <summary>
/// XML formatter. Elements whose content is only elements (and comments and processing instructions) get their
/// children on lines of their own, indented; the whitespace between them is all that changes. An element with text in
/// it (a "loc", a "title", SVG "text", mixed content), and everything under xml:space="preserve", is kept exactly as
/// written. Tags are kept as written, apart from runs of whitespace between attributes made one space. Comments, CDATA
/// sections, the XML declaration and the doctype are kept. A file that is not well-formed is not formatted.
/// </summary>
public sealed class XmlFormatter : IFormatter
{
    public bool CanFormat(DocumentKind kind) => kind == DocumentKind.Xml;

    public string Format(SourceDocument document, FormatOptions options)
    {
        var errors = XmlValidator.Validate(document.Path, document.Content)
            .FirstOrDefault(d => d.Code == "xml.not-well-formed");
        if (errors != null)
            throw new Core.FormatException($"{errors.Message} (line {errors.Start.Line})");

        var newline = options.NewLine ?? document.DetectNewLine();
        var root = XmlSourceReader.Read(document.Content);
        var lines = new List<string>();
        WriteChildren(document.Content, root.Children, 0, options.IndentUnit, lines, preserveBlankLines: true);
        return lines.Count == 0 ? string.Empty : string.Join(newline, lines.Select(l => Newlines(l, newline))) + newline;
    }

    private static string Newlines(string text, string newline) =>
        text.Contains('\n') || text.Contains('\r') ? text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", newline) : text;

    private static void WriteChildren(string source, List<SourceNode> children, int depth, string indentUnit, List<string> lines,
        bool preserveBlankLines)
    {
        var indent = string.Concat(Enumerable.Repeat(indentUnit, depth));
        var blank = false;
        var first = true;
        foreach (var child in children)
        {
            if (child is SourceText { IsWhitespace: true } space)
            {
                // One empty line in the source between two children is kept
                if (preserveBlankLines && !first && CountNewlines(space.Raw) >= 2) blank = true;
                continue;
            }

            if (blank) lines.Add(string.Empty);
            blank = false;
            first = false;

            switch (child)
            {
                case SourceElement element:
                    WriteElement(source, element, depth, indentUnit, lines);
                    break;
                default:
                    lines.Add(indent + child.Raw);
                    break;
            }
        }
    }

    private static void WriteElement(string source, SourceElement element, int depth, string indentUnit, List<string> lines)
    {
        var indent = string.Concat(Enumerable.Repeat(indentUnit, depth));
        var start = NormalizeTag(element.StartTag);
        if (element.SelfClosing)
        {
            lines.Add(indent + start);
            return;
        }

        var elementOnly = !element.PreservesSpace &&
                          element.Children.All(c => c is SourceElement or SourceComment or SourceInstruction || c is SourceText { IsWhitespace: true });
        if (!elementOnly)
        {
            // Text in it: kept exactly as written
            lines.Add(indent + start + source[element.ContentStart..element.ContentEnd] + element.EndTag);
            return;
        }

        if (element.Children.All(c => c is SourceText))
        {
            lines.Add(indent + start + element.EndTag);
            return;
        }

        lines.Add(indent + start);
        WriteChildren(source, element.Children, depth + 1, indentUnit, lines, preserveBlankLines: true);
        lines.Add(indent + element.EndTag);
    }

    // A tag with runs of whitespace outside the attribute values made one space (and none before ">" or "/>" but the
    // one kept before "/>")
    private static string NormalizeTag(string tag)
    {
        var result = new StringBuilder(tag.Length);
        char? quote = null;
        var space = false;
        foreach (var c in tag)
        {
            if (quote != null)
            {
                result.Append(c);
                if (c == quote) quote = null;
                continue;
            }

            if (c is ' ' or '\t' or '\n' or '\r')
            {
                space = true;
                continue;
            }

            // (no space is kept before ">"; one before "/>" is kept)
            if (space && c != '>') result.Append(' ');
            space = false;
            if (c is '"' or '\'') quote = c;
            result.Append(c);
        }

        return result.ToString();
    }

    private static int CountNewlines(string text) => text.Replace("\r\n", "\n").Count(c => c is '\n' or '\r');
}

// A lightweight tree of the XML source, which keeps the source text of every part (as the HTML formatter's)
internal abstract class SourceNode
{
    public string Raw { get; init; } = string.Empty;
}

internal sealed class SourceText : SourceNode
{
    public bool IsWhitespace => Raw.All(c => c is ' ' or '\t' or '\n' or '\r');
}

internal sealed class SourceComment : SourceNode;

/// <summary>A processing instruction or the XML declaration.</summary>
internal sealed class SourceInstruction : SourceNode;

/// <summary>A CDATA section or a doctype.</summary>
internal sealed class SourceVerbatim : SourceNode;

internal sealed class SourceElement : SourceNode
{
    public string StartTag { get; init; } = string.Empty;
    public string EndTag { get; set; } = string.Empty;
    public bool SelfClosing { get; init; }
    public bool PreservesSpace { get; set; }
    public List<SourceNode> Children { get; } = [];
    public int ContentStart { get; init; }
    public int ContentEnd { get; set; }
}

/// <summary>Reads well-formed XML into an <see cref="SourceElement"/> tree with the source text of every part.</summary>
internal static class XmlSourceReader
{
    public static SourceElement Read(string s)
    {
        var root = new SourceElement();
        var stack = new Stack<SourceElement>();
        stack.Push(root);
        var i = 0;
        while (i < s.Length)
        {
            var current = stack.Peek();
            if (s[i] != '<')
            {
                var end = s.IndexOf('<', i);
                if (end < 0) end = s.Length;
                current.Children.Add(new SourceText { Raw = s[i..end] });
                i = end;
                continue;
            }

            int close;
            if (At(s, i, "<!--"))
            {
                close = s.IndexOf("-->", i + 4, StringComparison.Ordinal) + 3;
                current.Children.Add(new SourceComment { Raw = s[i..close] });
            }
            else if (At(s, i, "<![CDATA["))
            {
                close = s.IndexOf("]]>", i, StringComparison.Ordinal) + 3;
                current.Children.Add(new SourceVerbatim { Raw = s[i..close] });
            }
            else if (At(s, i, "<?"))
            {
                close = s.IndexOf("?>", i, StringComparison.Ordinal) + 2;
                current.Children.Add(new SourceInstruction { Raw = s[i..close] });
            }
            else if (At(s, i, "<!"))
            {
                // A doctype, with an internal subset in [ ] if it has one
                var bracket = s.IndexOf('[', i);
                var gt = s.IndexOf('>', i);
                close = (bracket >= 0 && bracket < gt ? s.IndexOf('>', s.IndexOf(']', bracket)) : gt) + 1;
                current.Children.Add(new SourceVerbatim { Raw = s[i..close] });
            }
            else if (At(s, i, "</"))
            {
                close = s.IndexOf('>', i) + 1;
                current.EndTag = s[i..close];
                current.ContentEnd = i;
                stack.Pop();
            }
            else
            {
                close = TagEnd(s, i) + 1;
                var tag = s[i..close];
                var selfClosing = tag.EndsWith("/>", StringComparison.Ordinal);
                var element = new SourceElement { StartTag = tag, SelfClosing = selfClosing, ContentStart = close };
                element.PreservesSpace = current.PreservesSpace;
                if (tag.Contains("xml:space", StringComparison.Ordinal))
                    element.PreservesSpace = tag.Contains("\"preserve\"", StringComparison.Ordinal) || tag.Contains("'preserve'", StringComparison.Ordinal);
                current.Children.Add(element);
                if (!selfClosing) stack.Push(element);
            }

            i = close;
        }

        return root;
    }

    private static bool At(string s, int i, string text) => string.CompareOrdinal(s, i, text, 0, text.Length) == 0;

    // The ">" that ends a tag (not one inside a quoted attribute value)
    private static int TagEnd(string s, int start)
    {
        char? quote = null;
        for (var i = start + 1; i < s.Length; i++)
        {
            var c = s[i];
            if (quote != null)
            {
                if (c == quote) quote = null;
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }

        return s.Length - 1;
    }
}
