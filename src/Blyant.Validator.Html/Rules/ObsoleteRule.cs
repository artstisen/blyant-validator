using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// Obsolete elements and attributes (HTML standard, section 16), and attributes that are no longer needed. Messages as
/// the Nu Html Checker's.
/// </summary>
public sealed class ObsoleteRule : IHtmlRule
{
    public void Check(HtmlRuleContext context)
    {
        foreach (var element in context.Root.Descendants().Where(e => !e.IsForeign))
        {
            if (HtmlSpec.ObsoleteElements.TryGetValue(element.Lower, out var instead))
            {
                context.Report(element, Severity.Error, "html.obsolete-element",
                    $"The “{element.Lower}” element is obsolete. {instead}");
                continue;
            }

            foreach (var attribute in element.Attributes)
                CheckAttribute(context, element, attribute);
        }
    }

    private static void CheckAttribute(HtmlRuleContext context, HElement element, HAttribute attribute)
    {
        var name = attribute.Name.ToLowerInvariant();
        var value = attribute.Value.Trim();

        // Obsolete but conforming: a warning only (HTML standard, 16.1)
        if ((element.Lower, name) is ("img", "border") && value == "0" ||
            (element.Lower, name) is ("table", "border") && value is "" or "1")
        {
            context.Report(attribute.Start, Severity.Warning, "html.obsolete-attribute",
                $"The “{name}” attribute on the “{element.Lower}” element is obsolete. Use CSS instead.");
            return;
        }

        if (HtmlSpec.ObsoleteAttributes.TryGetValue(element.Lower + " " + name, out var instead) ||
            HtmlSpec.ObsoleteAttributes.TryGetValue("* " + name, out instead))
        {
            context.Report(attribute.Start, Severity.Error, "html.obsolete-attribute",
                $"The “{name}” attribute on the “{element.Lower}” element is obsolete. {instead}");
            return;
        }

        // Not needed: the default is the same
        if (element.Lower == "script" && name == "type" && IsJavaScript(value))
            context.Report(attribute.Start, Severity.Warning, "html.unnecessary-type",
                "The “type” attribute is unnecessary for JavaScript resources.");
        if (element.Lower == "style" && name == "type" && value.Equals("text/css", StringComparison.OrdinalIgnoreCase))
            context.Report(attribute.Start, Severity.Warning, "html.unnecessary-type",
                "The “type” attribute for the “style” element is not needed and should be omitted.");
    }

    private static bool IsJavaScript(string type) => type.ToLowerInvariant() is
        "text/javascript" or "application/javascript" or "application/ecmascript" or "text/ecmascript" or
        "text/jscript" or "text/livescript" or "application/x-javascript" or "text/x-javascript";
}
