using System.Text.RegularExpressions;
using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// Links within the page (href="#intro") to an id that is not there: a warning, as the link goes nowhere. "#" and
/// "#top" go to the top of the page without an id. Not the routes of a web app ("#/about", "#!/home") or text
/// fragments ("#:~:text=…"), which are not ids. Not in a fragment (the page around it may have the id), nor in a page
/// with &lt;base href&gt; (where "#intro" goes to the base).
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
            if (element.GetAttribute("id") is { } id) ids.Add(Links.Decode(id.Value));
            if (element.Lower == "a" && element.GetAttribute("name") is { } name) ids.Add(Links.Decode(name.Value));
        }

        foreach (var link in all.Where(e => e.Lower is "a" or "area" && !e.IsForeign))
        {
            if (link.GetAttribute("href") is not { } href || !href.Value.StartsWith('#')) continue;
            var target = Links.Decode(href.Value[1..]);
            if (target.Length == 0 || target.Equals("top", StringComparison.OrdinalIgnoreCase) || Templates.IsTemplate(target) ||
                target[0] is '/' or '!' || target.Contains(":~:", StringComparison.Ordinal))
                continue;
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
public sealed partial class FileLinkRule : IHtmlRule
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
        // (the names of each folder, asked once for the page)
        var names = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
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

                if (context.Options.NamesIn is { } namesIn && CaseOf(file, folder, namesIn, names) is { } written)
                    context.Report(attribute.ValueContentStart, Severity.Warning, "html.file-case",
                        $"The file is written “{written}”, not “{file}”: a server that tells case apart (Linux) does not find it.");
                else if (!exists(full))
                    context.Report(attribute.ValueContentStart, Severity.Warning, "html.missing-file", $"The file “{file}” is not there.");
            }
        }
    }
}

public sealed partial class FileLinkRule
{
    /// <summary>
    /// The path of a link as the files are written, when a name of it differs from the name of the file or folder only
    /// in case ("Images/Logo.png" for images/logo.png); null when the names are the same, or a name is not found (then
    /// the file is missing, or cannot be looked at). Only the names of the link are compared, not the folders above
    /// the page
    /// </summary>
    internal static string? CaseOf(string file, string folder, Func<string, IReadOnlyList<string>> namesIn,
        Dictionary<string, IReadOnlyList<string>> cache)
    {
        var current = folder;
        var written = new List<string>();
        var differs = false;
        foreach (var name in file.Split('/'))
        {
            if (name is "" or ".")
            {
                written.Add(name);
                continue;
            }

            if (name == "..")
            {
                written.Add(name);
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            if (!cache.TryGetValue(current, out var names)) cache[current] = names = namesIn(current);
            var match = names.FirstOrDefault(n => string.Equals(n, name, StringComparison.Ordinal)) ??
                        names.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (match == null) return null;
            differs |= match != name;
            written.Add(match);
            current = Path.Combine(current, match);
        }

        return differs ? string.Join('/', written) : null;
    }
}

/// <summary>
/// An unquoted value right before "/&gt;" of a void element: &lt;img src=a.png alt=x/&gt; makes the alt text "x/", as
/// the "/" is read as part of the value. A warning: put a space before "/&gt;", or quote the value. Not a link or a web
/// address, where a "/" at the end is normal (a minified &lt;link href=https://example.com/&gt;).
/// </summary>
public sealed class UnquotedSlashRule : IHtmlRule
{
    // Attributes whose value is a link, which may end with "/"
    private static readonly HashSet<string> LinkAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "href", "action", "formaction", "cite", "content", "data", "manifest",
    };

    public void Check(HtmlRuleContext context)
    {
        foreach (var element in context.Root.Descendants().Where(e => e.IsVoid && !e.IsForeign && !e.SelfClosing))
        {
            if (element.Attributes is not [.., { RawValue: { Length: > 1 } raw } last] || raw[0] is '"' or '\'' || !raw.EndsWith('/')) continue;
            if (raw.Contains("://", StringComparison.Ordinal) || LinkAttributes.Contains(last.Name)) continue;
            context.Report(last.Start, Severity.Warning, "html.unquoted-slash",
                $"The value of “{last.Name}” is “{raw}”: the “/” of “/>” is read as part of the value. Put a space before “/>”, or quote the value.");
        }
    }
}

/// <summary>What the link rules share: which links are relative paths to files, and decoding them.</summary>
internal static partial class Links
{
    /// <summary>The path of a relative link to a file, without its query and fragment, decoded; null for other links.</summary>
    public static string? RelativeFile(string value)
    {
        // (character references are decoded first, as browsers do: "Caf&eacute;.html", "a&#35;b.html")
        value = Decode(value).Trim();
        if (value.Length == 0 || value[0] is '/' or '\\' or '#' or '?' || Scheme().IsMatch(value) || Templates.IsTemplate(value)) return null;
        // (a "\" is read as "/", as browsers do: on Linux and macOS it would be a part of the name)
        var file = Unescape(value.Split('#', '?')[0].Replace('\\', '/'));
        return file.Length == 0 || file.Contains('\0') ? null : file;
    }

    /// <summary>A value with its character references decoded (&amp;amp;, &amp;eacute;, &amp;#32;), as browsers read it.</summary>
    public static string Decode(string value) => value.Contains('&') ? System.Net.WebUtility.HtmlDecode(value) : value;

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
