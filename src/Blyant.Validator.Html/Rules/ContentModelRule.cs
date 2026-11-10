using System.Collections.Frozen;
using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// Content models (HTML standard, 3.2.5): which elements may contain which. A selection of the most common cases:
/// lists, definition lists, tables, select, head and picture; where li, dt/dd, table parts, option, figcaption,
/// legend, summary, source and track may stand; flow content (div, p, ul …) inside elements that only take phrasing
/// content (span, p, h1 …); interactive content inside a and button; text where only elements go; and more than one
/// visible main. Custom elements, SVG and MathML are not checked. Messages as the Nu Html Checker's.
/// </summary>
public sealed class ContentModelRule : IHtmlRule
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static FrozenSet<string> Set(string words) => words.Split(' ').ToFrozenSet(Ci);

    /// <summary>Flow content that is not phrasing content: blocks.</summary>
    private static readonly FrozenSet<string> FlowOnly = Set(
        "address article aside blockquote details dialog div dl fieldset figure footer form h1 h2 h3 h4 h5 h6 header " +
        "hgroup hr main menu nav ol p pre search section table ul");

    /// <summary>Elements whose content is phrasing content only.</summary>
    private static readonly FrozenSet<string> PhrasingParents = Set(
        "p h1 h2 h3 h4 h5 h6 pre span b i em strong small s cite q dfn abbr code var samp kbd sub sup u mark bdi bdo " +
        "data time label button output ruby rt");

    /// <summary>Elements whose content model is that of their parent (transparent).</summary>
    private static readonly FrozenSet<string> Transparent = Set("a ins del map object video audio canvas noscript slot");

    /// <summary>The children an element may have (besides whitespace).</summary>
    private static readonly FrozenDictionary<string, FrozenSet<string>> AllowedChildren =
        new Dictionary<string, string>(Ci)
        {
            ["ul"] = "li script template",
            ["ol"] = "li script template",
            ["menu"] = "li script template",
            ["dl"] = "dt dd div script template",
            ["table"] = "caption colgroup thead tbody tfoot tr script template",
            ["thead"] = "tr script template",
            ["tbody"] = "tr script template",
            ["tfoot"] = "tr script template",
            ["tr"] = "td th script template",
            ["colgroup"] = "col template",
            ["select"] = "option optgroup hr script template button",
            ["optgroup"] = "option script template legend",
            ["head"] = "base link meta noscript script style template title",
            ["html"] = "head body",
            ["picture"] = "source img script template",
        }.ToFrozenDictionary(p => p.Key, p => Set(p.Value), Ci);

    /// <summary>The parents an element may have.</summary>
    private static readonly FrozenDictionary<string, FrozenSet<string>> AllowedParents =
        new Dictionary<string, string>(Ci)
        {
            ["li"] = "ul ol menu",
            ["dt"] = "dl div",
            ["dd"] = "dl div",
            ["tr"] = "table thead tbody tfoot",
            ["td"] = "tr",
            ["th"] = "tr",
            ["thead"] = "table",
            ["tbody"] = "table",
            ["tfoot"] = "table",
            ["caption"] = "table",
            ["colgroup"] = "table",
            ["col"] = "colgroup table",
            ["option"] = "select datalist optgroup",
            ["optgroup"] = "select",
            ["figcaption"] = "figure",
            ["legend"] = "fieldset optgroup",
            ["summary"] = "details",
            ["source"] = "picture audio video",
            ["track"] = "audio video",
        }.ToFrozenDictionary(p => p.Key, p => Set(p.Value), Ci);

    /// <summary>Elements in which text (other than whitespace) is not allowed.</summary>
    private static readonly FrozenSet<string> NoText = Set("ul ol menu dl colgroup select optgroup picture html head");

    public void Check(HtmlRuleContext context)
    {
        Walk(context, context.Root, []);

        var mains = context.Root.Descendants().Where(e => e.Lower == "main" && !e.IsForeign && !e.HasAttribute("hidden")).ToList();
        foreach (var extra in mains.Skip(1))
            context.Report(extra, Severity.Error, "html.content-model",
                "A document must not include more than one visible “main” element.");
    }

    private static void Walk(HtmlRuleContext context, HElement parent, List<HElement> ancestors)
    {
        foreach (var child in parent.Children)
        {
            if (child is HText text)
            {
                if (NoText.Contains(parent.Lower) && !parent.IsForeign && text.Raw.Any(c => !char.IsWhiteSpace(c)))
                    context.Report(text.Start + text.Raw.TakeWhile(char.IsWhiteSpace).Count(), Severity.Error,
                        "html.text-not-allowed", $"Text not allowed in element “{parent.Lower}” in this context.");
                continue;
            }

            if (child is not HElement element) continue;
            if (!element.IsForeign && !IsUnchecked(element)) CheckElement(context, element, parent, ancestors);
            ancestors.Add(element);
            Walk(context, element, ancestors);
            ancestors.RemoveAt(ancestors.Count - 1);
        }
    }

    // Custom elements, and elements that are not HTML (or obsolete: told about by ObsoleteRule)
    private static bool IsUnchecked(HElement element) =>
        element.Lower.Contains('-') || !HtmlSpec.Elements.ContainsKey(element.Lower);

    private static void CheckElement(HtmlRuleContext context, HElement element, HElement parent, List<HElement> ancestors)
    {
        var name = element.Lower;
        var parentName = parent.Lower;
        // (a template holds content for elsewhere, and the document itself any element: a fragment)
        var parentChecked = parentName is not ("#root" or "template") && !parent.IsForeign && !IsUnchecked(parent);

        void NotAllowed() => context.Report(element, Severity.Error, "html.content-model",
            $"Element “{name}” not allowed as child of element “{parentName}” in this context.");

        if (parentChecked && AllowedChildren.TryGetValue(parentName, out var children) && !children.Contains(name))
        {
            NotAllowed();
            return;
        }

        if (parentChecked && AllowedParents.TryGetValue(name, out var parents) &&
            (!parents.Contains(parentName) || name is "dt" or "dd" && parentName == "div" && !IsInDl(ancestors)))
        {
            NotAllowed();
            return;
        }

        // A block inside an element that only takes phrasing content (looking through transparent elements)
        if (FlowOnly.Contains(name) && parentChecked && PhrasingContainer(parent, ancestors) is not null)
        {
            NotAllowed();
            return;
        }

        // Interactive content inside a or button
        if (IsInteractive(element))
        {
            var container = ancestors.LastOrDefault(a => a.Lower is "a" or "button" && !a.IsForeign);
            if (container != null && !(container.Lower == name))
                context.Report(element, Severity.Error, "html.content-model",
                    $"The element “{name}” must not appear as a descendant of the “{container.Lower}” element.");
        }
    }

    // The div of a dl (dt and dd may be in a div that is a child of dl)
    private static bool IsInDl(List<HElement> ancestors) => ancestors.Count >= 2 && ancestors[^2].Lower == "dl";

    // The element whose phrasing content model applies to a child of "parent": the parent, or above transparent
    // elements the first one that is not transparent. Null when flow content is allowed
    private static HElement? PhrasingContainer(HElement parent, List<HElement> ancestors)
    {
        var current = parent;
        var index = ancestors.Count - 1;
        while (Transparent.Contains(current.Lower))
        {
            index--;
            if (index < 0) return null;
            current = ancestors[index];
        }

        return PhrasingParents.Contains(current.Lower) ? current : null;
    }

    private static bool IsInteractive(HElement element) => element.Lower switch
    {
        "a" => element.HasAttribute("href"),
        "button" or "details" or "embed" or "iframe" or "label" or "select" or "textarea" => true,
        "input" => !string.Equals(element.GetAttribute("type")?.Value.Trim(), "hidden", StringComparison.OrdinalIgnoreCase),
        "audio" or "video" => element.HasAttribute("controls"),
        "img" => element.HasAttribute("usemap"),
        _ => false,
    };
}
