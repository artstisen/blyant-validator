using System.Text.RegularExpressions;

namespace Blyant.Validator.Html.Rules;

/// <summary>
/// Values that a template fills in when the page is made (Jekyll, Hugo, Jinja, Liquid, PHP, ERB, Thymeleaf, Template
/// Toolkit, Create React App …): not a value of HTML yet, so it is not checked (a language, a charset, a link).
/// </summary>
public static partial class Templates
{
    public static bool IsTemplate(string value) => Marker().IsMatch(value);

    // {{ }}, {% %}, {# #}, <? ?>, <% %>, ${ }, #{ }, *{ }, @{ }, [% %], %PUBLIC_URL%, {name}
    [GeneratedRegex(@"\{\{|\{%|\{#|<\?|<%|[$#*@]\{|\[%|%[A-Z_][A-Z0-9_]*%|\{[A-Za-z_][A-Za-z0-9_.]*\}")]
    private static partial Regex Marker();
}
