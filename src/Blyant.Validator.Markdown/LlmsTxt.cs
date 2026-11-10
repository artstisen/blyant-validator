using System.Text.RegularExpressions;
using Blyant.Validator.Core;

namespace Blyant.Validator.Markdown;

/// <summary>
/// The llms.txt format (https://llmstxt.org/), built in (nothing is fetched): an H1 with the name of the project or
/// site first, then an optional blockquote with a summary and optional text, then sections under H2 headings that are
/// lists of links, “- [name](url)”, each optionally followed by “: notes”. Breaking the format is an error.
/// </summary>
internal static partial class LlmsTxt
{
    public static void Check(MarkdownReader reader, MarkdownValidator.Reporter report)
    {
        var content = reader.Lines.Where(l => l.Kind != LineKind.Blank).ToList();
        if (content.Count == 0 || content[0] is not { Kind: LineKind.Heading, Level: 1 })
        {
            report(content.Count == 0 ? 1 : content[0].Number, 1, Severity.Error, "llms.missing-h1",
                "An llms.txt file must start with a level 1 heading: the name of the project or site (“# Name”).");
        }

        var h1Seen = false;
        var inSection = false;
        var previous = LineKind.Blank;
        var urls = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in reader.Lines)
        {
            if (line.Kind == LineKind.Heading)
            {
                if (line.Level == 1)
                {
                    if (h1Seen)
                        report(line.Number, 1, Severity.Error, "llms.multiple-h1",
                            "An llms.txt file has one level 1 heading, the name of the project or site: use “##” for sections.");
                    h1Seen = true;
                }
                else if (line.Level == 2)
                {
                    inSection = true;
                }
                else
                {
                    report(line.Number, 1, Severity.Error, "llms.heading-level",
                        $"Only “#” (the name) and “##” (sections) are headings of the llms.txt format, not level {line.Level}.");
                }

                previous = line.Kind;
                continue;
            }

            if (!inSection || line.Kind == LineKind.Blank)
            {
                previous = line.Kind == LineKind.Blank ? previous : line.Kind;
                continue;
            }

            // In a section: list items with a link first; a wrapped note (indented) goes on from the item above
            if (line.Kind == LineKind.ListItem)
            {
                if (FileListItem().Match(line.Text) is not { Success: true } item)
                {
                    report(line.Number, 1, Severity.Error, "llms.list-item",
                        "A list item of an llms.txt section must start with a link: “- [name](url)”, then “: notes” if wanted.");
                }
                else
                {
                    var url = item.Groups[2].Value;
                    var column = item.Groups[2].Index + 1;
                    if (CheckUrl(url) is { } problem) report(line.Number, column, Severity.Error, "llms.bad-url", problem);
                    else if (!urls.TryAdd(url, line.Number))
                        report(line.Number, column, Severity.Warning, "llms.duplicate-url",
                            $"“{Shorten(url)}” is listed more than once (first on line {urls[url]}).");
                }
            }
            else if (!(previous == LineKind.ListItem && line.Kind == LineKind.Text && char.IsWhiteSpace(line.Text[0])))
            {
                report(line.Number, 1, Severity.Error, "llms.section-content",
                    "A section of an llms.txt file (under “##”) is a list of links: “- [name](url): notes”.");
                continue;
            }

            if (line.Kind == LineKind.ListItem) previous = LineKind.ListItem;
        }
    }

    // Why a URL of a file list is not usable, or null: an absolute web address, or a relative path, without spaces
    private static string? CheckUrl(string url)
    {
        if (url.Length == 0) return "The link has no URL.";
        if (url.Any(char.IsWhiteSpace)) return $"“{Shorten(url)}” contains a space: use “%20” instead.";
        if (Scheme().IsMatch(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is "http" or "https" && uri.Host.Length == 0)
                return $"“{Shorten(url)}” is not a valid URL.";
            return null;
        }

        return Uri.IsWellFormedUriString(url, UriKind.Relative) || Uri.TryCreate(url, UriKind.Relative, out _)
            ? null
            : $"“{Shorten(url)}” is not a valid URL.";
    }

    private static string Shorten(string text) => text.Length > 60 ? text[..60] + "…" : text;

    // "- [name](url): notes", also indented (a list in a list), with brackets in the name and parentheses in the URL
    // (one level: "[a [b]]", "https://en.wikipedia.org/wiki/Foo_(bar)")
    [GeneratedRegex(@"^\s*[-*+]\s+\[((?:[^\[\]]|\[[^\[\]]*\])+)\]\(((?:[^()]|\([^()]*\))*)\)(\s*:\s*.*)?\s*$")]
    private static partial Regex FileListItem();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex Scheme();
}
