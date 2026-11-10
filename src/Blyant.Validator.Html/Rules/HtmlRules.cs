using Blyant.Validator.Core;

namespace Blyant.Validator.Html.Rules;

/// <summary>A single conformance check run against a parsed HTML document.</summary>
public interface IHtmlRule
{
    void Check(HtmlRuleContext context);
}

/// <summary>The rules enabled by default. New rules are added here.</summary>
public static class HtmlRuleSet
{
    public static IReadOnlyList<IHtmlRule> Default { get; } =
    [
        new RequireTitleRule(),
        new RequireLangRule(),
        new ImgAltRule(),
        new DuplicateIdRule(),
        new EmbeddedCssRule(),
        new ObsoleteRule(),
        new AttributeRule(),
        new ContentModelRule(),
    ];
}

/// <summary>A document must have a non-empty &lt;title&gt; in &lt;head&gt;.</summary>
public sealed class RequireTitleRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        var title = context.Find("title");
        if (title is null)
            context.Report(context.Find("head") ?? context.Find("html"), Severity.Error, "html.head-missing-title",
                "Element “head” is missing a required instance of child element “title”.");
        else if (string.IsNullOrWhiteSpace(title.TextContent))
            context.Report(title, Severity.Error, "html.title-empty", "Element “title” must not be empty.");
    }
}

/// <summary>The root &lt;html&gt; element should declare the document language.</summary>
public sealed class RequireLangRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        var root = context.Find("html");
        if (string.IsNullOrWhiteSpace(root?.GetAttribute("lang")?.Value))
            context.Report(root, Severity.Warning, "html.missing-lang",
                "Consider adding a “lang” attribute to the “html” start tag to declare the language of this document.");
    }
}

/// <summary>&lt;img&gt; elements must have an alt attribute (it may be empty for decorative images).</summary>
public sealed class ImgAltRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var img in context.FindAll("img"))
        {
            if (!img.HasAttribute("alt"))
                context.Report(img, Severity.Error, "html.img-missing-alt",
                    "An “img” element must have an “alt” attribute, except under certain conditions.");
        }
    }
}

/// <summary>id values must be unique within a document.</summary>
public sealed class DuplicateIdRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in context.Root.Descendants())
        {
            var id = element.GetAttribute("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;
            if (!seen.Add(id))
                context.Report(element, Severity.Error, "html.duplicate-id", $"Duplicate ID “{id}”.");
        }
    }
}
