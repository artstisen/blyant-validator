using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// The accessibility hints (<see cref="HtmlRuleSet.Accessibility"/>): checks of the kind Lighthouse and axe make, which
/// are not errors of HTML. They are info, run only when asked for (--accessibility, :accessibility), and their codes
/// start with "html.a11y.", so they can be ignored together (html.a11y.*).
/// </summary>
public static class Accessible
{
    /// <summary>
    /// True when an element has a name a screen reader can read: text, an image with alt text, aria-label,
    /// aria-labelledby or title (on it, or on an element in it)
    /// </summary>
    public static bool HasName(HElement element)
    {
        if (HasText(element, "aria-label") || HasText(element, "aria-labelledby") || HasText(element, "title")) return true;
        foreach (var child in element.Children)
        {
            switch (child)
            {
                case HText text when !string.IsNullOrWhiteSpace(text.Raw):
                    return true;
                case HElement { Lower: "img" or "area" } image when HasText(image, "alt") || HasText(image, "aria-label"):
                    return true;
                case HElement { Lower: "svg" } svg when HasText(svg, "aria-label") ||
                                                         svg.Children.OfType<HElement>().Any(e => e.Lower == "title" && !string.IsNullOrWhiteSpace(e.TextContent)):
                    return true;
                case HElement { Lower: "input" } input when input.GetAttribute("type")?.Value.Trim().ToLowerInvariant() == "image" && HasText(input, "alt"):
                    return true;
                case HElement inner when HasName(inner):
                    return true;
            }
        }

        return false;
    }

    public static bool HasText(HElement element, string attribute) =>
        element.GetAttribute(attribute) is { } a && !string.IsNullOrWhiteSpace(a.Value);
}

/// <summary>Links and buttons without a name: a screen reader reads the address of the link, or only "button".</summary>
public sealed class NameRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var element in context.Root.Descendants().Where(e => !e.IsForeign))
        {
            if (element.Lower == "a" && element.HasAttribute("href") && !Accessible.HasName(element))
                context.Report(element, Severity.Info, "html.a11y.link-name",
                    "The link has no text: a screen reader reads the address instead. Give it text, an image with alt text, or aria-label.");
            else if (element.Lower == "button" && !Accessible.HasName(element))
                context.Report(element, Severity.Info, "html.a11y.button-name",
                    "The button has no text: a screen reader only says “button”. Give it text, an image with alt text, or aria-label.");
        }
    }
}

/// <summary>
/// Form fields without a label: a &lt;label for&gt;, a &lt;label&gt; around the field, aria-label, aria-labelledby or
/// title. A placeholder is not a label (it is gone once something is typed).
/// </summary>
public sealed class LabelRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        var labelled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var label in context.FindAll("label"))
        {
            if (label.GetAttribute("for") is { } target) labelled.Add(target.Value);
        }

        Walk(context.Root, insideLabel: false, labelled, context);
    }

    private static void Walk(HElement element, bool insideLabel, HashSet<string> labelled, HtmlRuleContext context)
    {
        foreach (var child in element.Children.OfType<HElement>().Where(e => !e.IsForeign))
        {
            if (IsField(child) && !insideLabel && !(child.GetAttribute("id") is { } id && labelled.Contains(id.Value)) &&
                !Accessible.HasText(child, "aria-label") && !Accessible.HasText(child, "aria-labelledby") && !Accessible.HasText(child, "title"))
                context.Report(child, Severity.Info, "html.a11y.label",
                    $"The “{child.Lower}” field has no label: a screen reader cannot tell what it is for. Add a “label”, or aria-label.");
            Walk(child, insideLabel || child.Lower == "label", labelled, context);
        }
    }

    // Fields that are filled in (not buttons, which have their text, and not hidden fields)
    private static bool IsField(HElement element) => element.Lower switch
    {
        "select" or "textarea" => true,
        "input" => element.GetAttribute("type")?.Value.Trim().ToLowerInvariant() is not ("hidden" or "submit" or "reset" or "button" or "image"),
        _ => false,
    };
}

/// <summary>Heading levels that skip (an h3 after an h1): readers who move from heading to heading miss a level.</summary>
public sealed class HeadingOrderRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        var last = 0;
        foreach (var heading in context.Root.Descendants().Where(e => !e.IsForeign && e.Lower is ['h', >= '1' and <= '6']))
        {
            var level = heading.Lower[1] - '0';
            // (the first heading may be at any level: the page may be a part of a larger whole)
            if (last > 0 && level > last + 1)
                context.Report(heading, Severity.Info, "html.a11y.heading-order",
                    $"The heading level jumps from h{last} to h{level}: use h{last + 1}.");
            last = level;
        }
    }
}

/// <summary>An &lt;iframe&gt; without a title: a screen reader cannot tell what the frame holds.</summary>
public sealed class FrameTitleRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var frame in context.FindAll("iframe"))
        {
            if (!Accessible.HasText(frame, "title") && !Accessible.HasText(frame, "aria-label") && !Accessible.HasText(frame, "aria-labelledby"))
                context.Report(frame, Severity.Info, "html.a11y.frame-title",
                    "The “iframe” has no title: a screen reader cannot tell what the frame holds. Add title=\"…\".");
        }
    }
}

/// <summary>
/// A page without &lt;meta name="viewport"&gt;: phones show it as a small desktop page, and text must be zoomed to be
/// read. Not in a fragment (the page around it has the head).
/// </summary>
public sealed class ViewportRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        if (context.Options.Fragment || context.Find("html") is not { } html) return;
        if (context.FindAll("meta").Any(m => m.GetAttribute("name")?.Value.Trim().Equals("viewport", StringComparison.OrdinalIgnoreCase) == true)) return;
        context.Report(context.Find("head") ?? html, Severity.Info, "html.a11y.viewport",
            "The page has no <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">: phones show it as a small desktop page.");
    }
}
