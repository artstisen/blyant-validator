using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// A role that an element has anyway (&lt;button role="button"&gt;, &lt;nav role="navigation"&gt;): a warning, as the Nu
/// Html Checker gives it. Lists are left out: role="list" on a &lt;ul&gt; is a known fix for Safari, which drops the
/// role of a list without bullets.
/// </summary>
public sealed class RedundantRoleRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var element in context.Root.Descendants().Where(e => !e.IsForeign))
        {
            if (element.GetAttribute("role") is not { } role) continue;
            // (the first of the roles given is the one used)
            var first = role.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
            if (first != null && first == ImplicitRole(element))
                context.Report(role.Start, Severity.Warning, "html.redundant-role",
                    $"The “{first}” role is unnecessary for element “{element.Lower}”: it has that role anyway.");
        }
    }

    // The role an element has without a role attribute, for the elements where that does not depend on where it is
    private static string? ImplicitRole(HElement element) => element.Lower switch
    {
        "a" or "area" when element.HasAttribute("href") => "link",
        "article" => "article",
        "aside" => "complementary",
        "button" => "button",
        "dialog" => "dialog",
        "h1" or "h2" or "h3" or "h4" or "h5" or "h6" => "heading",
        "hr" => "separator",
        "main" => "main",
        "nav" => "navigation",
        "table" => "table",
        "textarea" => "textbox",
        "input" => element.GetAttribute("type")?.Value.Trim().ToLowerInvariant() switch
        {
            "checkbox" => "checkbox",
            "radio" => "radio",
            "button" or "submit" or "reset" => "button",
            "range" => "slider",
            _ => null,
        },
        _ => null,
    };
}

/// <summary>
/// A &lt;section&gt; or &lt;article&gt; without a heading (h1 to h6, or an hgroup, not counting those of the sections
/// inside it): a warning, as the Nu Html Checker gives it. One named with aria-label or aria-labelledby is not told.
/// </summary>
public sealed class SectionHeadingRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var section in context.Root.Descendants().Where(e => e.Lower is "section" or "article" && !e.IsForeign))
        {
            if (section.HasAttribute("aria-label") || section.HasAttribute("aria-labelledby") || HasHeading(section)) continue;
            var name = section.Lower == "section" ? "Section" : "Article";
            context.Report(section, Severity.Warning, "html.section-no-heading",
                $"{name} lacks heading. Use “h2” to “h6” for a heading, or a “div” where no heading is needed.");
        }
    }

    // A heading in the section, not in a nested section
    private static bool HasHeading(HElement element)
    {
        foreach (var child in element.Children.OfType<HElement>())
        {
            if (child.Lower is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "hgroup") return true;
            if (child.Lower is "section" or "article" or "aside" or "nav") continue;
            if (HasHeading(child)) return true;
        }

        return false;
    }
}
