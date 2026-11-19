using System.Text.RegularExpressions;
using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// Links within the page (href="#intro") to an id that is not there: a warning, as the link goes nowhere. "#" and
/// "#top" go to the top of the page without an id. Not in a fragment (the page around it may have the id), nor in a
/// page with &lt;base href&gt; (where "#intro" goes to the base).
/// </summary>
public sealed class AnchorRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        if (context.Options.Fragment || context.Find("base")?.HasAttribute("href") == true) return;
        var all = context.Root.Descendants().ToList();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in all)
        {
            if (element.GetAttribute("id") is { } id) ids.Add(id.Value);
            if (element.Lower == "a" && element.GetAttribute("name") is { } name) ids.Add(name.Value);
        }

        foreach (var link in all.Where(e => e.Lower is "a" or "area" && !e.IsForeign))
        {
            if (link.GetAttribute("href") is not { } href || !href.Value.StartsWith('#')) continue;
            var target = href.Value[1..];
            if (target.Length == 0 || target.Equals("top", StringComparison.OrdinalIgnoreCase) || Links.IsTemplate(target)) continue;
            if (ids.Contains(target) || ids.Contains(Links.Unescape(target))) continue;
            context.Report(href.ValueContentStart, Severity.Warning, "html.broken-anchor",
                $"There is no element with id “{target}” in this page: the link goes nowhere.");
        }
    }
}

/// <summary>
/// Relative links, images, scripts and style sheets to files that are not there (as the Markdown checks do), looked up
/// through <see cref="HtmlCheckOptions.Exists"/>. Only relative paths: not web addresses, not paths from the root of
/// the site ("/css/site.css": the root is not known), not in a fragment (its links are those of the published page),
/// nor in a page with &lt;base href&gt;.
/// </summary>
public sealed class FileLinkRule : IHtmlRule
{
    private static readonly (string Element, string Attribute)[] Targets =
    [
        ("a", "href"), ("area", "href"), ("link", "href"), ("img", "src"), ("script", "src"), ("iframe", "src"),
        ("source", "src"), ("video", "src"), ("audio", "src"), ("track", "src"), ("embed", "src"), ("input", "src"),
        ("video", "poster"), ("object", "data"),
    ];

    public void Check(HtmlRuleContext context)
    {
        if (context.Options.Exists is not { } exists || context.Options.Fragment) return;
        if (context.Find("base")?.HasAttribute("href") == true) return;
        var folder = Path.GetDirectoryName(Path.GetFullPath(context.Document.Path)) ?? ".";
        foreach (var element in context.Root.Descendants().Where(e => !e.IsForeign))
        {
            foreach (var (name, attributeName) in Targets)
            {
                if (element.Lower != name || element.GetAttribute(attributeName) is not { } attribute) continue;
                if (Links.RelativeFile(attribute.Value) is not { } file) continue;
                string full;
                try
                {
                    full = Path.GetFullPath(file, folder);
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    continue;
                }

                if (!exists(full))
                    context.Report(attribute.ValueContentStart, Severity.Warning, "html.missing-file", $"The file “{file}” is not there.");
            }
        }
    }
}

/// <summary>
/// An unquoted value right before "/&gt;" of a void element: &lt;img src=a.png alt=x/&gt; makes the alt text "x/", as
/// the "/" is read as part of the value. A warning: put a space before "/&gt;", or quote the value.
/// </summary>
public sealed class UnquotedSlashRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var element in context.Root.Descendants().Where(e => e.IsVoid && !e.IsForeign && !e.SelfClosing))
        {
            if (element.Attributes is not [.., { RawValue: { Length: > 1 } raw } last] || raw[0] is '"' or '\'' || !raw.EndsWith('/')) continue;
            context.Report(last.Start, Severity.Warning, "html.unquoted-slash",
                $"The value of “{last.Name}” is “{raw}”: the “/” of “/>” is read as part of it. Put a space before “/>”, or quote the value.");
        }
    }
}

/// <summary>What the link rules share: which links are relative paths to files, and decoding them.</summary>
internal static partial class Links
{
    /// <summary>The path of a relative link to a file, without its query and fragment, decoded; null for other links.</summary>
    public static string? RelativeFile(string value)
    {
        value = value.Trim();
        if (value.Length == 0 || value[0] is '/' or '\\' or '#' or '?' || Scheme().IsMatch(value) || IsTemplate(value)) return null;
        // (a "\" is read as "/", as browsers do: on Linux and macOS it would be a part of the name)
        var file = Unescape(value.Split('#', '?')[0].Replace('\\', '/'));
        return file.Length == 0 || file.Contains('\0') ? null : file;
    }

    /// <summary>A value made by a template (Jekyll, Hugo, PHP …), which is not a link yet.</summary>
    public static bool IsTemplate(string value) =>
        value.Contains("{{", StringComparison.Ordinal) || value.Contains("{%", StringComparison.Ordinal) ||
        value.Contains("<?", StringComparison.Ordinal) || value.Contains("<%", StringComparison.Ordinal) ||
        value.Contains("${", StringComparison.Ordinal);

    public static string Unescape(string text)
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

    // https:, mailto:, tel:, data:, javascript: …
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex Scheme();
}
