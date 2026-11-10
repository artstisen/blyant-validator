using System.Text.RegularExpressions;
using Blyant.Validator.Core;

namespace Blyant.Validator.Markdown;

/// <summary>
/// Native Markdown validator. Markdown has no invalid documents, so its checks are warnings about what probably does
/// not show as meant: code blocks never closed, headings that are not headings, skipped heading levels, links that are
/// not closed, references that are not defined, anchors and files that are not there. llms.txt files are also checked
/// against the llms.txt format (https://llmstxt.org/), whose rules are built in: nothing is fetched.
/// </summary>
/// <param name="exists">Tells whether a file or directory is there, for relative links (null: they are not checked).
/// The validator itself does not touch the file system.</param>
public sealed partial class MarkdownValidator(Func<string, bool>? exists = null) : IValidator
{
    public string Engine => "native";

    public bool CanValidate(DocumentKind kind) => kind == DocumentKind.Markdown;

    public Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default) =>
        Task.FromResult(Validate(document.Path, document.Content, exists));

    public static IReadOnlyList<Diagnostic> Validate(string path, string text, Func<string, bool>? exists = null)
    {
        var diagnostics = new List<Diagnostic>();
        var reader = MarkdownReader.Read(text);
        var name = Path.GetFileName(path).ToLowerInvariant();

        void Report(int line, int column, Severity severity, string code, string message)
        {
            var at = new SourcePosition(line, column);
            diagnostics.Add(new Diagnostic(path, severity, code, message, at, at));
        }

        foreach (var problem in reader.Problems)
            Report(problem.Line, problem.Column, Severity.Warning, problem.Code, problem.Message);

        CheckHeadings(reader, Report, allowSeveralH1: name == "llms-full.txt");
        CheckLinks(reader, path, exists, Report);
        if (name == "llms.txt") LlmsTxt.Check(reader, Report);

        return [.. diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column)];
    }

    internal delegate void Reporter(int line, int column, Severity severity, string code, string message);

    private static void CheckHeadings(MarkdownReader reader, Reporter report, bool allowSeveralH1)
    {
        var lastLevel = 0;
        var h1Count = 0;
        foreach (var line in reader.Lines)
        {
            // (not "#!" on the first line: a script's first line)
            if (line.Kind == LineKind.Text && NoSpaceHeading().IsMatch(line.Text) &&
                !(line.Number == 1 && line.Text.StartsWith("#!", StringComparison.Ordinal)))
            {
                report(line.Number, 1, Severity.Warning, "md.heading-space",
                    "This line is not a heading: put a space after the “#”.");
                continue;
            }

            if (line.Kind != LineKind.Heading) continue;
            if (line.HeadingText.Length == 0)
                report(line.Number, 1, Severity.Warning, "md.empty-heading", "Empty heading.");
            if (line.Level == 1 && ++h1Count > 1 && !allowSeveralH1)
                report(line.Number, 1, Severity.Warning, "md.multiple-h1",
                    "More than one level 1 heading: a document usually has one title.");
            if (lastLevel > 0 && line.Level > lastLevel + 1)
                report(line.Number, 1, Severity.Warning, "md.heading-skip",
                    $"The heading level jumps from {lastLevel} to {line.Level}: use level {lastLevel + 1}.");
            lastLevel = line.Level;
        }
    }

    private static void CheckLinks(MarkdownReader reader, string path, Func<string, bool>? exists, Reporter report)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        foreach (var link in reader.Links)
        {
            var what = link.IsImage ? "image" : "link";
            if (link.Label != null)
            {
                if (!reader.Definitions.ContainsKey(MarkdownReader.NormalizeLabel(link.Label)))
                    report(link.Line, link.Column, Severity.Warning, "md.undefined-reference",
                        $"The reference “[{link.Label}]” is not defined: add “[{link.Label}]: url” to the file.");
                continue;
            }

            var target = link.Target ?? string.Empty;
            if (target.Length == 0)
            {
                report(link.Line, link.Column, Severity.Warning, "md.empty-link", $"The {what} “[{Shorten(link.Text)}]()” has no target.");
                continue;
            }

            if (target.StartsWith('#'))
            {
                var anchor = Unescape(target[1..]);
                if (anchor.Length > 0 && !reader.Anchors.Contains(anchor) && !reader.Anchors.Contains(anchor.ToLowerInvariant()))
                    report(link.Line, link.Column, Severity.Warning, "md.broken-anchor",
                        $"There is no heading or anchor “{anchor}” in this file.");
                continue;
            }

            // Only relative paths are checked: not web addresses (https:, mailto: …), not paths from the site root
            if (exists == null || Scheme().IsMatch(target) || target.StartsWith('/') || target.StartsWith('\\')) continue;
            var file = target.Split('#', '?')[0];
            if (file.Length == 0) continue;
            var full = Path.GetFullPath(Path.Combine(directory, Unescape(file)));
            if (!exists(full))
                report(link.Line, link.Column, Severity.Warning, "md.broken-link",
                    $"The {what} target “{Shorten(file)}” does not exist.");
        }
    }

    private static string Unescape(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return text;
        }
    }

    private static string Shorten(string text) => text.Length > 40 ? text[..40] + "…" : text;

    [GeneratedRegex(@"^ {0,3}#{1,6}[^#\s]")]
    private static partial Regex NoSpaceHeading();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex Scheme();
}
