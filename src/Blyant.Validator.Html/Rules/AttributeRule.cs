using System.Text.RegularExpressions;
using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// Elements and attributes: unknown elements and attributes, the values of boolean, enumerated, numeric, ID and URL
/// attributes, attributes that are required, and labels whose "for" does not point at an element. Messages as the Nu
/// Html Checker's. Obsolete elements and attributes are left to <see cref="ObsoleteRule"/>.
/// </summary>
public sealed partial class AttributeRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        var ids = context.Root.Descendants()
            .Select(e => e.GetAttribute("id")?.Value)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);
        Walk(context, context.Root, ids);
    }

    private static void Walk(HtmlRuleContext context, HElement parent, HashSet<string?> ids)
    {
        foreach (var element in parent.Children.OfType<HElement>())
        {
            if (!element.IsForeign) CheckElement(context, element, parent, ids);
            Walk(context, element, ids);
        }
    }

    private static void CheckElement(HtmlRuleContext context, HElement element, HElement parent, HashSet<string?> ids)
    {
        var name = element.Lower;
        if (HtmlSpec.ObsoleteElements.ContainsKey(name)) return;
        var custom = name.Contains('-');
        if (!custom && !HtmlSpec.Elements.ContainsKey(name))
        {
            context.Report(element, Severity.Error, "html.unknown-element",
                $"Element “{name}” not allowed as child of element “{ParentName(parent)}” in this context.");
            return;
        }

        foreach (var attribute in element.Attributes) CheckAttribute(context, element, attribute, custom);
        CheckRequired(context, element);

        if (name == "label" && element.GetAttribute("for") is { } target && !ids.Contains(target.Value))
            context.Report(target.Start, Severity.Error, "html.label-for",
                "The value of the “for” attribute of the “label” element must be the ID of a non-hidden form control.");
    }

    private static string ParentName(HElement parent) => parent.Lower == "#root" ? "#document" : parent.Lower;

    private static void CheckAttribute(HtmlRuleContext context, HElement element, HAttribute attribute, bool custom)
    {
        var name = attribute.Name.ToLowerInvariant();
        var el = element.Lower;
        var value = attribute.Value;

        // Namespaced (xml:lang, xlink:href), data-*, aria-* and event handlers (onclick)
        if (name.Contains(':') || name.StartsWith("data-", StringComparison.Ordinal) ||
            name.StartsWith("aria-", StringComparison.Ordinal) || EventHandler().IsMatch(name))
            return;

        var known = HtmlSpec.GlobalAttributes.Contains(name) ||
                    HtmlSpec.Elements.TryGetValue(el, out var own) && own.Contains(name) ||
                    HtmlSpec.ObsoleteAttributes.ContainsKey("* " + name);
        if (!known)
        {
            if (!custom)
                context.Report(attribute.Start, Severity.Error, "html.unknown-attribute",
                    $"Attribute “{name}” not allowed on element “{el}” at this point.");
            return;
        }

        // Obsolete attributes are told about by ObsoleteRule
        if (HtmlSpec.ObsoleteAttributes.ContainsKey(el + " " + name)) return;

        if (attribute.RawValue == null) value = string.Empty;
        string? problem = null;
        if (HtmlSpec.BooleanAttributes.Contains(name))
        {
            if (value.Length > 0 && !value.Equals(name, StringComparison.OrdinalIgnoreCase)) problem = string.Empty;
        }
        else if (el == "ol" && name == "type")
        {
            // The only enumerated attribute whose values depend on case
            if (value is not ("1" or "a" or "A" or "i" or "I")) problem = string.Empty;
        }
        else if (HtmlSpec.EnumeratedAttributes.TryGetValue(el + " " + name, out var values) ||
                 HtmlSpec.EnumeratedAttributes.TryGetValue("* " + name, out values))
        {
            var trimmed = value.Trim();
            if (!(trimmed.Length == 0 ? HtmlSpec.EmptyAllowed.Contains(name) : values.Contains(trimmed)))
                problem = string.Empty;
        }
        else if (name == "id")
        {
            if (value.Length == 0) problem = ": An ID must not be the empty string.";
            else if (value.Any(IsSpace)) problem = ": An ID must not contain whitespace.";
        }
        else if (NumberRule(el, name) is { } number)
        {
            problem = number(value);
        }
        else if (HtmlSpec.UrlAttributes.Contains(el + "." + name))
        {
            var url = value.Trim(' ', '\t', '\n', '\r', '\f');
            if (!url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            {
                if (url.Any(IsSpace)) problem = ": Illegal character in URL: space is not allowed. Use “%20” instead.";
                else if (url.Contains('\\')) problem = ": Backslash (“\\”) used as path segment delimiter. Use “/” instead.";
            }
        }

        if (problem != null)
            context.Report(attribute.ValueStart >= 0 ? attribute.ValueStart : attribute.Start, Severity.Error,
                "html.bad-value", $"Bad value “{Shorten(value)}” for attribute “{name}” on element “{el}”{problem}" +
                                  (problem.Length == 0 ? "." : string.Empty));
    }

    // The attributes that hold numbers, and how they are checked (null: fine, else the end of the message)
    private static Func<string, string?>? NumberRule(string element, string attribute) => (element, attribute) switch
    {
        ("img" or "video" or "canvas" or "iframe" or "embed" or "object" or "source" or "input", "width" or "height") =>
            NonNegative,
        (_, "maxlength" or "minlength") => NonNegative,
        ("input" or "select", "size") or ("textarea", "cols" or "rows") or ("col" or "colgroup", "span") => Positive,
        ("td" or "th", "colspan") => v => Positive(v) ?? (int.Parse(v) > 1000 ? ": The value must be 1000 or less." : null),
        ("td" or "th", "rowspan") => v => NonNegative(v) ?? (long.Parse(v) > 65534 ? ": The value must be 65534 or less." : null),
        (_, "tabindex") or ("ol", "start") or ("li", "value") => Integer,
        _ => null,
    };

    private static string? NonNegative(string value) =>
        value.Length > 0 && value.Length < 10 && value.All(char.IsAsciiDigit) ? null : ": Expected a non-negative integer.";

    private static string? Positive(string value) =>
        NonNegative(value) is null && value.Any(c => c != '0') ? null : ": Expected a positive integer.";

    private static string? Integer(string value) =>
        NonNegative(value.StartsWith('-') ? value[1..] : value) is null ? null : ": Expected an integer.";

    private static void CheckRequired(HtmlRuleContext context, HElement element)
    {
        bool Has(string name) => element.HasAttribute(name);
        void Missing(string attributes) =>
            context.Report(element, Severity.Error, "html.missing-attribute",
                attributes.Contains(' ')
                    ? $"Element “{element.Lower}” is missing one or more of the following attributes: " +
                      string.Join(", ", attributes.Split(' ').Select(a => $"“{a}”")) + "."
                    : $"Element “{element.Lower}” is missing required attribute “{attributes}”.");

        switch (element.Lower)
        {
            case "img" when !Has("src") && !Has("srcset"):
                Missing("src srcset");
                break;
            case "link":
                if (!Has("href") && !Has("imagesrcset")) Missing("href");
                if (!Has("rel") && !Has("itemprop"))
                    context.Report(element, Severity.Error, "html.missing-attribute",
                        "A “link” element must have a “rel” or “itemprop” attribute, or both.");
                break;
            case "meta":
                if (!Has("name") && !Has("http-equiv") && !Has("charset") && !Has("itemprop") && !Has("property"))
                    Missing("charset content http-equiv itemprop name property");
                else if ((Has("name") || Has("http-equiv") || Has("property")) && !Has("content"))
                    Missing("content");
                break;
            case "area" when Has("href") && !Has("alt"):
                Missing("alt");
                break;
            case "optgroup" when !Has("label"):
                Missing("label");
                break;
            case "track" when !Has("src"):
                Missing("src");
                break;
            case "map" when !Has("name"):
                Missing("name");
                break;
            case "base" when !Has("href") && !Has("target"):
                Missing("href target");
                break;
            case "bdo" when !Has("dir"):
                Missing("dir");
                break;
            case "object" when !Has("data") && !Has("type"):
                Missing("data type");
                break;
            case "meter" when !Has("value"):
                Missing("value");
                break;
            case "input" when element.GetAttribute("type")?.Value.Trim().Equals("image", StringComparison.OrdinalIgnoreCase) == true:
                if (!Has("src")) Missing("src");
                if (string.IsNullOrWhiteSpace(element.GetAttribute("alt")?.Value))
                    context.Report(element, Severity.Error, "html.missing-attribute",
                        "An “input” element with a “type” attribute whose value is “image” must have an “alt” attribute whose value is not the empty string.");
                break;
        }
    }

    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    private static string Shorten(string value) => value.Length > 40 ? value[..40] + "…" : value;

    [GeneratedRegex("^on[a-z]+$")]
    private static partial Regex EventHandler();
}
