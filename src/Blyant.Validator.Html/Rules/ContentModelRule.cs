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
            // (a col right in a table is in a colgroup whose start tag is left out)
            ["table"] = "caption colgroup col thead tbody tfoot tr script template",
            ["thead"] = "tr script template",
            ["tbody"] = "tr script template",
            ["tfoot"] = "tr script template",
            ["tr"] = "td th script template",
            ["colgroup"] = "col template",
            // (customizable select: a button first, and div around options)
            ["select"] = "option optgroup hr script template button div noscript",
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
            // (a div in a customizable select can hold options)
            ["option"] = "select datalist optgroup div",
            ["optgroup"] = "select",
            ["figcaption"] = "figure",
            ["legend"] = "fieldset optgroup",
            ["summary"] = "details",
            ["source"] = "picture audio video",
            ["track"] = "audio video",
        }.ToFrozenDictionary(p => p.Key, p => Set(p.Value), Ci);

    /// <summary>Elements that must not have these elements as descendants (HTML standard, content models).</summary>
    private static readonly FrozenDictionary<string, FrozenSet<string>> ForbiddenDescendants =
        new Dictionary<string, string>(Ci)
        {
            ["header"] = "header footer",
            ["footer"] = "header footer",
            ["address"] = "address header footer h1 h2 h3 h4 h5 h6 hgroup article aside nav section",
            ["dfn"] = "dfn",
            ["label"] = "label",
            ["meter"] = "meter",
            ["progress"] = "progress",
            ["article"] = "main",
            ["aside"] = "main",
            ["nav"] = "main",
        }.ToFrozenDictionary(p => p.Key, p => Set(p.Value), Ci);

    /// <summary>Form controls that a label labels.</summary>
    private static readonly FrozenSet<string> Labelable = Set("button input meter output progress select textarea");

    private static readonly FrozenSet<string> Headings = Set("h1 h2 h3 h4 h5 h6");

    /// <summary>Elements in which text (other than whitespace) is not allowed.</summary>
    private static readonly FrozenSet<string> NoText = Set("ul ol menu dl colgroup select optgroup picture html head");

    public void Check(HtmlRuleContext context)
    {
        Walk(context, context.Root, []);

        var mains = context.Root.Descendants().Where(e => e.Lower == "main" && !e.IsForeign && !e.HasAttribute("hidden")).ToList();
        foreach (var extra in mains.Skip(1))
            context.Report(extra, Severity.Error, "html.content-model",
                "A document must not include more than one visible “main” element.");

        foreach (var element in context.Root.Descendants().Where(e => !e.IsForeign))
        {
            switch (element.Lower)
            {
                case "label":
                    CheckLabel(context, element);
                    break;
                case "hgroup":
                    CheckHgroup(context, element);
                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6" when IsEmptyHeading(element):
                    context.Report(element, Severity.Warning, "html.empty-heading", "Empty heading.");
                    break;
            }
        }
    }

    // A label labels one control: the one inside it (which must have the ID of "for", if it has one)
    private static void CheckLabel(HtmlRuleContext context, HElement label)
    {
        var controls = label.Descendants()
            .Where(e => !e.IsForeign && Labelable.Contains(e.Lower) &&
                        !string.Equals(e.GetAttribute("type")?.Value.Trim(), "hidden", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (controls.Count > 1)
            context.Report(controls[1], Severity.Error, "html.content-model",
                "The “label” element may contain at most one “button”, “input”, “meter”, “output”, “progress”, “select”, or “textarea” descendant.");

        if (label.GetAttribute("for")?.Value is { } target && controls.Count > 0 &&
            controls[0].GetAttribute("id")?.Value != target)
            context.Report(controls[0], Severity.Error, "html.content-model",
                $"Any “{controls[0].Lower}” descendant of a “label” element with a “for” attribute must have an ID value that matches that “for” attribute.");
    }

    // An hgroup holds one heading, and paragraphs before or after it
    private static void CheckHgroup(HtmlRuleContext context, HElement hgroup)
    {
        var children = hgroup.Children.OfType<HElement>().Where(e => e.Lower is not ("script" or "template")).ToList();
        var headings = children.Count(e => Headings.Contains(e.Lower));
        if (headings != 1)
            context.Report(hgroup, Severity.Error, "html.content-model", headings == 0
                ? "Element “hgroup” is missing a required child element: a heading (“h1” … “h6”)."
                : "An “hgroup” element must contain only one heading (“h1” … “h6”).");
        foreach (var other in children.Where(e => !Headings.Contains(e.Lower) && e.Lower != "p"))
            context.Report(other, Severity.Error, "html.content-model",
                $"Element “{other.Lower}” not allowed as child of element “hgroup” in this context.");
    }

    // A heading without text (an image with alt text counts as text)
    private static bool IsEmptyHeading(HElement heading) =>
        heading.TextContent.Trim().Length == 0 &&
        !heading.Descendants().Any(e => e.Lower == "img" && !string.IsNullOrWhiteSpace(e.GetAttribute("alt")?.Value) ||
                                        e.Lower == "svg" || e.IsForeign);

    private static void Walk(HtmlRuleContext context, HElement parent, List<HElement> ancestors)
    {
        foreach (var child in parent.Children)
        {
            if (child is HText text)
            {
                if (NoText.Contains(parent.Lower) && !parent.IsForeign && text.Raw.Any(c => !char.IsWhiteSpace(c)) &&
                    !(parent.Lower == "html" && BodyOf(parent) is null))
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

    private static HElement? BodyOf(HElement html) => html.Children.OfType<HElement>().FirstOrDefault(e => e.Lower == "body");

    private static bool IsHeadContent(string name) => AllowedChildren["head"].Contains(name);

    private static void CheckElement(HtmlRuleContext context, HElement element, HElement parent, List<HElement> ancestors)
    {
        var name = element.Lower;
        var parentName = parent.Lower;
        // (a template holds content for elsewhere, and the document itself any element: a fragment)
        var parentChecked = parentName is not ("#root" or "template") && !parent.IsForeign && !IsUnchecked(parent);

        void NotAllowed() => context.Report(element, Severity.Error, "html.content-model",
            $"Element “{name}” not allowed as child of element “{parentName}” in this context.");

        // The start and end tags of head and body may be left out: in an html without a body element, its other
        // children are in the head and the body that are left out; before a body, metadata is in the head
        if (parentName == "html" && BodyOf(parent) is { } body ? element.Start < body.Start && IsHeadContent(name) : parentName == "html")
            parentChecked = false;

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

        // Elements that must not be inside some others (header in footer, h2 in address, main in article …)
        foreach (var ancestor in ancestors)
        {
            if (ancestor.IsForeign || !ForbiddenDescendants.TryGetValue(ancestor.Lower, out var forbidden) ||
                !forbidden.Contains(name))
                continue;
            context.Report(element, Severity.Error, "html.content-model",
                $"The element “{name}” must not appear as a descendant of the “{ancestor.Lower}” element.");
            return;
        }

        if (name == "main" && ancestors.LastOrDefault(a => a.Lower is "header" or "footer") is { } section)
        {
            context.Report(element, Severity.Error, "html.content-model",
                $"The element “main” must not appear as a descendant of the “{section.Lower}” element.");
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
