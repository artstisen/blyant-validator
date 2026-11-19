using System.Text;
using System.Text.RegularExpressions;
using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// The character encoding of a page, as the Nu Html Checker checks it: declared (a BOM, or &lt;meta charset&gt; or
/// http-equiv="Content-Type"), within the first 1024 bytes, once, and as UTF-8 (the program only reads UTF-8 files).
/// Without it a browser may guess another encoding, and letters such as æ, ø and é come out garbled. Not in a fragment:
/// the page around it declares the encoding.
/// </summary>
public sealed partial class CharsetRule : IHtmlRule
{
    // The HTML standard: the declaration must be in the first 1024 bytes of the file
    private const int MaxBytes = 1024;

    public void Check(HtmlRuleContext context)
    {
        if (context.Options.Fragment) return;
        var declarations = context.FindAll("meta").Select(m => (Meta: m, Charset: CharsetOf(m))).Where(d => d.Charset != null).ToList();
        if (declarations.Count == 0)
        {
            if (!context.Document.HasUtf8Bom)
                context.Report(context.Find("head") ?? context.Find("html"), Severity.Error, "html.charset-missing",
                    "No character encoding is declared: add <meta charset=\"utf-8\"> at the start of the “head”, or a browser may guess another encoding.");
            return;
        }

        var (first, charset) = declarations[0];
        var bytes = Encoding.UTF8.GetByteCount(context.Document.Content.AsSpan(0, Math.Min(first.ContentStart, context.Document.Content.Length)));
        if (bytes + (context.Document.HasUtf8Bom ? 3 : 0) > MaxBytes)
            context.Report(first, Severity.Error, "html.charset-late",
                $"The character encoding is declared after the first {MaxBytes} bytes of the file: move <meta charset> to the start of the “head”.");
        if (!charset!.Trim().Equals("utf-8", StringComparison.OrdinalIgnoreCase) && !charset.Trim().Equals("utf8", StringComparison.OrdinalIgnoreCase))
            context.Report(first, Severity.Error, "html.charset-mismatch",
                $"The character encoding is declared as “{charset.Trim()}”, but the file is UTF-8: declare <meta charset=\"utf-8\">.");
        foreach (var (meta, _) in declarations.Skip(1))
            context.Report(meta, Severity.Error, "html.charset-twice", "The character encoding is declared more than once: keep one <meta charset>.");
    }

    // charset="…", or http-equiv="Content-Type" content="text/html; charset=…"
    private static string? CharsetOf(HElement meta)
    {
        if (meta.GetAttribute("charset") is { } charset) return charset.Value;
        if (meta.GetAttribute("http-equiv")?.Value.Trim().Equals("content-type", StringComparison.OrdinalIgnoreCase) != true) return null;
        return CharsetParameter().Match(meta.GetAttribute("content")?.Value ?? "") is { Success: true } m ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@"charset\s*=\s*[""']?([^\s;""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex CharsetParameter();
}

/// <summary>A page has one &lt;title&gt; (in a fragment, a title is warned about by <see cref="FragmentRule"/>).</summary>
public sealed class TitleCountRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        if (context.Options.Fragment) return;
        foreach (var title in context.FindAll("title").Skip(1))
            context.Report(title, Severity.Error, "html.title-twice", "More than one “title” element: a page has one.");
    }
}

/// <summary>
/// The values of lang and xml:lang are language tags (BCP 47): "en", "da", "en-US", "zh-Hant", "sr-Latn-RS". Their
/// form is checked, not whether the language is in the registry. "" (the language is not known) is allowed.
/// </summary>
public sealed partial class LangRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var element in context.Root.Descendants())
        {
            foreach (var attribute in element.Attributes)
            {
                if (!attribute.Name.Equals("lang", StringComparison.OrdinalIgnoreCase) &&
                    !attribute.Name.Equals("xml:lang", StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = attribute.Value.Trim();
                if (value.Length == 0 || LanguageTag().IsMatch(value)) continue;
                context.Report(attribute.ValueContentStart, Severity.Error, "html.bad-lang", value.Contains('_') && LanguageTag().IsMatch(value.Replace('_', '-'))
                    ? $"“{value}” is not a language tag: write “{value.Replace('_', '-')}”, with a hyphen."
                    : $"“{value}” is not a language tag: use a code such as “en”, “da” or “en-US”.");
            }
        }
    }

    // language (2 or 3 letters, and extended subtags), script, region, variants, extensions, private use; or private
    // use alone (x-…), or the old tags that start with "i-"
    [GeneratedRegex(@"^(?:[A-Za-z]{2,3}(?:-[A-Za-z]{3}){0,3}(?:-[A-Za-z]{4})?(?:-(?:[A-Za-z]{2}|[0-9]{3}))?" +
                    @"(?:-(?:[A-Za-z0-9]{5,8}|[0-9][A-Za-z0-9]{3}))*(?:-[0-9A-WY-Za-wy-z](?:-[A-Za-z0-9]{2,8})+)*" +
                    @"(?:-[xX](?:-[A-Za-z0-9]{1,8})+)?|[xX](?:-[A-Za-z0-9]{1,8})+|[iI]-[A-Za-z]{3,8})$")]
    private static partial Regex LanguageTag();
}

/// <summary>
/// URLs that must not be empty (the Nu Html Checker: "Must be non-empty"): src of img, script, iframe …, href of link,
/// action of form. (An empty href of a link, &lt;a href=""&gt;, is the page itself, and is allowed.)
/// </summary>
public sealed class EmptyUrlRule : IHtmlRule
{
    private static readonly (string Element, string Attribute)[] NonEmpty =
    [
        ("img", "src"), ("script", "src"), ("iframe", "src"), ("source", "src"), ("video", "src"), ("audio", "src"),
        ("track", "src"), ("embed", "src"), ("link", "href"), ("form", "action"), ("video", "poster"), ("object", "data"),
    ];

    public void Check(HtmlRuleContext context)
    {
        foreach (var element in context.Root.Descendants().Where(e => !e.IsForeign))
        {
            foreach (var (name, attributeName) in NonEmpty)
            {
                if (element.Lower != name || element.GetAttribute(attributeName) is not { RawValue: not null } attribute) continue;
                if (attribute.Value.Trim().Length == 0)
                    context.Report(attribute.Start, Severity.Error, "html.empty-url",
                        $"The “{attributeName}” of “{name}” is empty: give the URL, or leave out the attribute.");
            }
        }
    }
}

/// <summary>
/// A &lt;script src&gt; with code in it: the browser runs the file, and never the code (only comments may be there).
/// </summary>
public sealed partial class ScriptSourceRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var script in context.FindAll("script"))
        {
            if (!script.HasAttribute("src") || !IsJavaScript(script)) continue;
            var code = Comments().Replace(script.RawContent ?? script.TextContent, "");
            if (!string.IsNullOrWhiteSpace(code))
                context.Report(script, Severity.Error, "html.script-src-content",
                    "A “script” with “src” runs the file only: the code inside it is never run. Move the code to a separate “script”.");
        }
    }

    // No type, a JavaScript type, or a module (a data block, type="text/template", may hold anything)
    private static bool IsJavaScript(HElement script)
    {
        var type = script.GetAttribute("type")?.Value.Trim().ToLowerInvariant() ?? "";
        return type is "" or "module" or "text/javascript" or "application/javascript" or "text/ecmascript" or "application/ecmascript";
    }

    [GeneratedRegex(@"/\*[\s\S]*?\*/|//[^\n]*")]
    private static partial Regex Comments();
}
