using System.Text;
using System.Text.RegularExpressions;

namespace Blyant.Validator.Markdown;

/// <summary>What a line of a Markdown file is.</summary>
public enum LineKind
{
    Blank,
    Text,
    Heading,
    ListItem,
    Quote,
    Rule,
    ReferenceDefinition,
    Html,

    /// <summary>Inside a code block (fenced or indented), or the fences themselves, or front matter: not read.</summary>
    Code,
}

/// <param name="Number">The line number, from 1.</param>
public sealed record MarkdownLine(int Number, string Text, LineKind Kind)
{
    /// <summary>For a heading: its level (1 to 6).</summary>
    public int Level { get; init; }

    /// <summary>For a heading: its text, without the #s.</summary>
    public string HeadingText { get; init; } = string.Empty;
}

/// <summary>A link or image: inline [text](target), or by reference [text][label].</summary>
/// <param name="Line">The line number, from 1.</param>
/// <param name="Column">The column of the "[" (or "!"), from 1.</param>
public sealed record MarkdownLink(int Line, int Column, string Text, string? Target, string? Label, bool IsImage);

/// <summary>A place where Markdown is probably not what was meant.</summary>
public sealed record MarkdownProblem(int Line, int Column, string Code, string Message);

/// <summary>
/// A small reader of Markdown (CommonMark, as far as the checks need it): lines are told apart as headings (ATX and
/// setext), list items, block quotes, rules, reference definitions, HTML and code (fenced, indented, and front
/// matter), and the links and images of the text are found with their places. It does not touch the file system.
/// </summary>
public sealed partial class MarkdownReader
{
    public List<MarkdownLine> Lines { get; } = [];
    public List<MarkdownLink> Links { get; } = [];
    public List<MarkdownProblem> Problems { get; } = [];

    /// <summary>The reference definitions ([label]: url), by their normalized label, with the line of the first.</summary>
    public Dictionary<string, (string Url, int Line)> Definitions { get; } = new(StringComparer.Ordinal);

    /// <summary>The anchors of the file: the headings (as GitHub makes them) and id/name attributes of HTML.</summary>
    public HashSet<string> Anchors { get; } = new(StringComparer.Ordinal);

    public static MarkdownReader Read(string text)
    {
        var reader = new MarkdownReader();
        reader.ReadLines(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        return reader;
    }

    private void ReadLines(string[] lines)
    {
        var start = 0;
        // Front matter (YAML) at the very top: --- to --- (or ...)
        if (lines.Length > 0 && lines[0].TrimEnd() == "---")
        {
            var end = Array.FindIndex(lines, 1, l => l.TrimEnd() is "---" or "...");
            if (end > 0)
            {
                for (var i = 0; i <= end; i++) Lines.Add(new MarkdownLine(i + 1, lines[i], LineKind.Code));
                start = end + 1;
            }
        }

        string? fence = null; // The fence that is open ("```" or "~~~~" …)
        var fenceLine = 0;
        var previous = LineKind.Blank;
        var inList = false; // Indented lines belong to a list item, not to code
        var listIndent = 0; // The column where the text of the last list item starts (0-based)
        var inIndentedCode = false;
        for (var i = start; i < lines.Length; i++)
        {
            var text = lines[i];
            var number = i + 1;

            if (fence != null)
            {
                Lines.Add(new MarkdownLine(number, text, LineKind.Code));
                if (FenceClose(text, fence)) fence = null;
                continue;
            }

            if (FenceOpen().Match(text) is { Success: true } open)
            {
                // A fence at the margin after a blank line ends a list
                if (previous == LineKind.Blank && Indent(text) < listIndent) inList = false;
                fence = open.Groups[1].Value;
                fenceLine = number;
                Lines.Add(new MarkdownLine(number, text, LineKind.Code));
                previous = LineKind.Code;
                continue;
            }

            if (text.Trim().Length == 0)
            {
                Lines.Add(new MarkdownLine(number, text, LineKind.Blank));
                previous = LineKind.Blank;
                continue;
            }

            // After a blank line, a line less indented than the text of the list item ends the list
            var indent = Indent(text);
            if (inList && previous == LineKind.Blank && indent < listIndent && !ListMarker().IsMatch(text)) inList = false;

            // Indented code: four columns more than the text it is in (the margin, or the text of a list item), after
            // a blank line (or more of it)
            var codeIndent = (inList ? listIndent : 0) + 4;
            if (indent >= codeIndent && (previous == LineKind.Blank || inIndentedCode))
            {
                Lines.Add(new MarkdownLine(number, text, LineKind.Code));
                inIndentedCode = true;
                previous = LineKind.Code;
                continue;
            }

            inIndentedCode = false;

            var kind = Classify(text, previous, out var level, out var headingText);
            if (kind == LineKind.Heading && level > 0 && headingText == null)
            {
                // Setext: the line below a paragraph makes the paragraph a heading
                var above = Lines[^1];
                Lines[^1] = above with { Kind = LineKind.Heading, Level = level, HeadingText = above.Text.Trim() };
                Lines.Add(new MarkdownLine(number, text, LineKind.Rule));
                previous = LineKind.Rule;
                continue;
            }

            // A list goes on until something less indented than its text comes after a blank line, or a heading or rule
            if (kind == LineKind.ListItem && ListMarker().Match(text) is { } marker)
            {
                // The text starts after the marker and 1 to 4 spaces (more: the text is indented code, at 1)
                inList = true;
                var markerEnd = marker.Groups[1].Index + marker.Groups[1].Length;
                var after = text[markerEnd..];
                var spaces = Indent(after);
                listIndent = markerEnd + (after.Trim().Length == 0 || spaces > 4 ? 1 : Math.Max(1, spaces));
            }
            else if (kind is LineKind.Heading or LineKind.Rule)
            {
                inList = false;
            }

            Lines.Add(new MarkdownLine(number, text, kind) { Level = level, HeadingText = headingText ?? string.Empty });
            previous = kind;
        }

        if (fence != null)
            Problems.Add(new MarkdownProblem(fenceLine, 1, "md.unclosed-fence",
                $"The code block opened with “{fence}” is never closed: the rest of the file is shown as code."));

        CollectDefinitionsAndAnchors();
        foreach (var line in Lines) FindLinks(line);
    }

    // The columns of blank space at the start of a line (a tab counts as up to the next multiple of 4)
    private static int Indent(string text)
    {
        var columns = 0;
        foreach (var c in text)
        {
            if (c == ' ') columns++;
            else if (c == '\t') columns += 4 - columns % 4;
            else break;
        }
        return columns;
    }

    private static bool FenceClose(string text, string fence)
    {
        var trimmed = text.TrimStart(' ');
        if (text.Length - trimmed.Length > 3) return false;
        var run = trimmed.TakeWhile(c => c == fence[0]).Count();
        return run >= fence.Length && trimmed[run..].Trim().Length == 0;
    }

    // What a line is (not code, not blank). A setext underline is told as a heading with no text of its own
    private static LineKind Classify(string text, LineKind previous, out int level, out string? headingText)
    {
        level = 0;
        headingText = null;
        if (AtxHeading().Match(text) is { Success: true } atx)
        {
            level = atx.Groups[1].Length;
            headingText = ClosingHashes().Replace(atx.Groups[2].Value, string.Empty).Trim();
            return LineKind.Heading;
        }

        if (previous == LineKind.Text && SetextUnderline().Match(text) is { Success: true } underline)
        {
            level = underline.Groups[1].Value[0] == '=' ? 1 : 2;
            return LineKind.Heading;
        }

        if (ThematicBreak().IsMatch(text)) return LineKind.Rule;
        if (ReferenceDefinition().IsMatch(text)) return LineKind.ReferenceDefinition;
        if (ListMarker().IsMatch(text)) return LineKind.ListItem;
        if (text.TrimStart().StartsWith('>')) return LineKind.Quote;
        if (text.TrimStart().StartsWith('<') && HtmlStart().IsMatch(text)) return LineKind.Html;
        return LineKind.Text;
    }

    private void CollectDefinitionsAndAnchors()
    {
        var slugs = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in Lines)
        {
            if (line.Kind == LineKind.Code) continue;
            if (line.Kind == LineKind.ReferenceDefinition && ReferenceDefinition().Match(line.Text) is { } definition)
            {
                var label = NormalizeLabel(definition.Groups[1].Value);
                if (!Definitions.TryAdd(label, (definition.Groups[2].Value.Trim('<', '>'), line.Number)))
                    Problems.Add(new MarkdownProblem(line.Number, 1, "md.duplicate-reference",
                        $"The reference “[{definition.Groups[1].Value}]” is defined more than once; only the first is used (line {Definitions[label].Line})."));
            }

            if (line.Kind == LineKind.Heading)
            {
                var slug = Slug(line.HeadingText);
                var count = slugs.GetValueOrDefault(slug);
                slugs[slug] = count + 1;
                Anchors.Add(count == 0 ? slug : $"{slug}-{count}");
            }

            foreach (Match id in HtmlId().Matches(line.Text)) Anchors.Add(id.Groups[2].Value);
        }
    }

    /// <summary>The anchor GitHub makes for a heading: lowercase, punctuation left out, spaces as "-".</summary>
    public static string Slug(string heading)
    {
        // The text of links and code, without their marks
        heading = InlineLink().Replace(heading, "$1");
        var slug = new StringBuilder();
        foreach (var c in heading.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_') slug.Append(c);
            else if (c == ' ') slug.Append('-');
        }

        return slug.ToString();
    }

    /// <summary>A reference label as it is matched: case and runs of whitespace do not matter.</summary>
    public static string NormalizeLabel(string label) =>
        string.Join(' ', label.Trim().Split((char[])[' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private void FindLinks(MarkdownLine line)
    {
        if (line.Kind is LineKind.Code or LineKind.Blank or LineKind.ReferenceDefinition) return;
        var text = BlankCodeSpans(line.Text);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++;
                continue;
            }

            if (text[i] != '[') continue;
            var image = i > 0 && text[i - 1] == '!';
            var close = MatchingBracket(text, i);
            if (close < 0) continue;
            var linkText = text[(i + 1)..close];
            var column = (image ? i : i + 1);

            if (close + 1 < text.Length && text[close + 1] == '(')
            {
                var end = MatchingParen(text, close + 1);
                if (end < 0)
                {
                    Problems.Add(new MarkdownProblem(line.Number, column, "md.unclosed-link",
                        $"The link “[{Shorten(linkText)}](…” is not closed with “)” on its line."));
                    break;
                }

                Links.Add(new MarkdownLink(line.Number, column, linkText, Destination(text[(close + 2)..end]), null, image));
                i = end;
            }
            else if (close + 1 < text.Length && text[close + 1] == '[')
            {
                var labelEnd = text.IndexOf(']', close + 2);
                if (labelEnd < 0) continue;
                var label = text[(close + 2)..labelEnd];
                Links.Add(new MarkdownLink(line.Number, column, linkText, null, label.Length == 0 ? linkText : label, image));
                i = labelEnd;
            }
            else
            {
                // A shortcut reference [label]: only a link if the label is defined (else it is text)
                i = close;
            }
        }
    }

    // The destination of an inline link: before a title ("…", '…' or (…)), without < >
    private static string Destination(string inside)
    {
        inside = inside.Trim();
        if (inside.StartsWith('<'))
        {
            var end = inside.IndexOf('>');
            return end < 0 ? inside[1..] : inside[1..end];
        }

        var space = inside.IndexOfAny([' ', '\t']);
        return space < 0 ? inside : inside[..space];
    }

    private static int MatchingBracket(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '\\') { i++; continue; }
            if (text[i] == '[') depth++;
            else if (text[i] == ']' && --depth == 0) return i;
        }
        return -1;
    }

    private static int MatchingParen(string text, int open)
    {
        var depth = 0;
        var inAngle = false;
        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\') { i++; continue; }
            if (c == '<' && i == open + 1) inAngle = true;
            else if (c == '>' && inAngle) inAngle = false;
            else if (inAngle) continue;
            else if (c == '(') depth++;
            else if (c == ')' && --depth == 0) return i;
        }
        return -1;
    }

    // Code spans (`code`, ``co`de``) made blank, so links in them are not found and the columns stay right
    private static string BlankCodeSpans(string text)
    {
        var chars = text.ToCharArray();
        var i = 0;
        while (i < chars.Length)
        {
            if (chars[i] != '`') { i++; continue; }
            var run = 0;
            while (i + run < chars.Length && chars[i + run] == '`') run++;
            var marker = new string('`', run);
            var close = text.IndexOf(marker, i + run, StringComparison.Ordinal);
            while (close >= 0 && close + run < text.Length && text[close + run] == '`')
                close = text.IndexOf(marker, close + run + 1, StringComparison.Ordinal);
            if (close < 0) { i += run; continue; }
            for (var k = i; k < close + run; k++) chars[k] = ' ';
            i = close + run;
        }
        return new string(chars);
    }

    private static string Shorten(string text) => text.Length > 30 ? text[..30] + "…" : text;

    [GeneratedRegex(@"^ {0,3}(`{3,}|~{3,})")]
    private static partial Regex FenceOpen();

    [GeneratedRegex(@"^ {0,3}(#{1,6})(?:[ \t]+(.*)|[ \t]*)$")]
    private static partial Regex AtxHeading();

    [GeneratedRegex(@"[ \t]+#+[ \t]*$|^#+[ \t]*$")]
    private static partial Regex ClosingHashes();

    [GeneratedRegex(@"^ {0,3}(=+|-+)[ \t]*$")]
    private static partial Regex SetextUnderline();

    [GeneratedRegex(@"^ {0,3}((\*[ \t]*){3,}|(-[ \t]*){3,}|(_[ \t]*){3,})$")]
    private static partial Regex ThematicBreak();

    [GeneratedRegex(@"^ {0,3}\[([^\]]+)\]:[ \t]*(<[^>]*>|\S+)")]
    private static partial Regex ReferenceDefinition();

    [GeneratedRegex(@"^[ \t]*([-*+]|\d{1,9}[.)])([ \t]|$)")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"^ {0,3}</?[A-Za-z][A-Za-z0-9-]*(\s|/?>|$)|^ {0,3}<!--")]
    private static partial Regex HtmlStart();

    [GeneratedRegex(@"\b(id|name)\s*=\s*[""']([^""']+)[""']")]
    private static partial Regex HtmlId();

    [GeneratedRegex(@"!?\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex InlineLink();
}
