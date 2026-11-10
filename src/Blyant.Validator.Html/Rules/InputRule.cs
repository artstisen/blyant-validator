using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;
using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// The attributes of an &lt;input&gt; for its type (HTML standard, 4.10.5, the table of which attributes apply to
/// which type), and the form of the values of the types that have one (numbers, dates, times, colors, URLs).
/// Messages as the Nu Html Checker's.
/// </summary>
public sealed partial class InputRule : IHtmlRule
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static FrozenSet<string> Set(string words) => words.Split(' ').ToFrozenSet(Ci);

    private const string TextLike = "text search url tel email password";
    private const string DateLike = "date month week time datetime-local";

    /// <summary>The attributes that only apply to some types, and those types.</summary>
    private static readonly FrozenDictionary<string, FrozenSet<string>> AppliesTo = new Dictionary<string, string>(Ci)
    {
        ["accept"] = "file",
        ["alt"] = "image",
        ["src"] = "image",
        ["height"] = "image",
        ["width"] = "image",
        ["formaction"] = "submit image",
        ["formenctype"] = "submit image",
        ["formmethod"] = "submit image",
        ["formnovalidate"] = "submit image",
        ["formtarget"] = "submit image",
        ["popovertarget"] = "submit image reset button",
        ["popovertargetaction"] = "submit image reset button",
        ["autocomplete"] = $"{TextLike} {DateLike} number range color hidden",
        ["checked"] = "checkbox radio",
        // (dirname applies to more types since 2023: all that submit text, and the buttons)
        ["dirname"] = $"{TextLike} hidden submit reset button",
        ["list"] = $"text search url tel email {DateLike} number range color",
        ["max"] = $"{DateLike} number range",
        ["min"] = $"{DateLike} number range",
        ["step"] = $"{DateLike} number range",
        ["maxlength"] = TextLike,
        ["minlength"] = TextLike,
        ["pattern"] = TextLike,
        ["size"] = TextLike,
        ["placeholder"] = $"{TextLike} number",
        ["multiple"] = "email file",
        ["readonly"] = $"{TextLike} {DateLike} number",
        ["required"] = $"{TextLike} {DateLike} number checkbox radio file",
        ["value"] = $"{TextLike} {DateLike} number range color hidden checkbox radio submit reset button",
        ["alpha"] = "color",
        ["colorspace"] = "color",
    }.ToFrozenDictionary(p => p.Key, p => Set(p.Value), Ci);

    public void Check(HtmlRuleContext context)
    {
        foreach (var input in context.Root.Descendants().Where(e => e.Lower == "input" && !e.IsForeign))
        {
            // An unknown type is reported by AttributeRule; it counts as text, as browsers take it
            var type = input.GetAttribute("type")?.Value.Trim().ToLowerInvariant() ?? "text";
            if (!HtmlSpec.EnumeratedAttributes["input type"].Contains(type)) type = "text";

            foreach (var attribute in input.Attributes)
            {
                var name = attribute.Name.ToLowerInvariant();
                if (AppliesTo.TryGetValue(name, out var types) && !types.Contains(type))
                {
                    context.Report(attribute.Start, Severity.Error, "html.input-attribute",
                        $"Attribute “{name}” is only allowed when the input type is " + Join(types) + ".");
                    continue;
                }

                if (Problem(type, name, attribute.Value, input.HasAttribute("alpha") || input.HasAttribute("colorspace")) is { } problem)
                    context.Report(attribute.ValueStart >= 0 ? attribute.ValueStart : attribute.Start, Severity.Error,
                        "html.bad-value", $"Bad value “{Shorten(attribute.Value)}” for attribute “{name}” on element “input”: {problem}");
            }
        }
    }

    private static string Join(IEnumerable<string> types)
    {
        var list = types.Order(StringComparer.Ordinal).Select(t => $"“{t}”").ToList();
        return list.Count == 1 ? list[0] : string.Join(", ", list[..^1]) + ", or " + list[^1];
    }

    // What is wrong with the value of min, max, step or value for the type, or null
    private static string? Problem(string type, string name, string value, bool hasCssColor)
    {
        if (name == "step")
            return value.Trim().Equals("any", StringComparison.OrdinalIgnoreCase) ||
                   double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var step) && step > 0 &&
                   FloatNumber().IsMatch(value)
                ? null
                : "Expected “any” or a positive number.";

        if (name is not ("min" or "max" or "value") || value.Length == 0) return null;
        return type switch
        {
            "number" or "range" => FloatNumber().IsMatch(value) ? null : "Expected a number (as “3” or “2.5”).",
            "date" => IsDate(value) ? null : "Expected a date (as “2026-11-10”).",
            "month" => MonthValue().Match(value) is { Success: true } m && int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) is >= 1 and <= 12
                ? null : "Expected a month (as “2026-11”).",
            "week" => WeekValue().Match(value) is { Success: true } w && int.Parse(w.Groups[1].Value, CultureInfo.InvariantCulture) is >= 1 and <= 53
                ? null : "Expected a week (as “2026-W46”).",
            "time" => IsTime(value) ? null : "Expected a time (as “14:30” or “14:30:15”).",
            "datetime-local" => DateTimeLocal().Match(value) is { Success: true } dt && IsDate(dt.Groups[1].Value) && IsTime(dt.Groups[2].Value)
                ? null : "Expected a date and time (as “2026-11-10T14:30”).",
            // (with alpha or colorspace, a color input takes any CSS color: not checked)
            "color" when name == "value" && !hasCssColor => ColorValue().IsMatch(value) ? null : "Expected a color as “#rrggbb” (as “#336699”).",
            "url" when name == "value" => Uri.TryCreate(value.Trim(), UriKind.Absolute, out _) ? null : "Expected an absolute URL.",
            _ => null,
        };
    }

    private static bool IsDate(string value) =>
        DateValue().IsMatch(value) &&
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    private static bool IsTime(string value) =>
        TimeValue().Match(value) is { Success: true } t &&
        int.Parse(t.Groups[1].Value, CultureInfo.InvariantCulture) <= 23 &&
        int.Parse(t.Groups[2].Value, CultureInfo.InvariantCulture) <= 59 &&
        (!t.Groups[3].Success || int.Parse(t.Groups[3].Value, CultureInfo.InvariantCulture) <= 59);

    private static string Shorten(string value) => value.Length > 40 ? value[..40] + "…" : value;

    // A valid floating-point number of HTML: -1, 2.5, .5, 1e3 (not "1." nor "+1")
    [GeneratedRegex(@"^-?([0-9]+(\.[0-9]+)?|\.[0-9]+)([eE][+-]?[0-9]+)?$")]
    private static partial Regex FloatNumber();

    [GeneratedRegex(@"^[0-9]{4,}-[0-9]{2}-[0-9]{2}$")]
    private static partial Regex DateValue();

    [GeneratedRegex(@"^[0-9]{4,}-([0-9]{2})$")]
    private static partial Regex MonthValue();

    [GeneratedRegex(@"^[0-9]{4,}-W([0-9]{2})$")]
    private static partial Regex WeekValue();

    [GeneratedRegex(@"^([0-9]{2}):([0-9]{2})(?::([0-9]{2})(?:\.[0-9]{1,3})?)?$")]
    private static partial Regex TimeValue();

    [GeneratedRegex(@"^([0-9]{4,}-[0-9]{2}-[0-9]{2})[T ]([0-9]{2}:[0-9]{2}(?::[0-9]{2}(?:\.[0-9]{1,3})?)?)$")]
    private static partial Regex DateTimeLocal();

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorValue();
}
