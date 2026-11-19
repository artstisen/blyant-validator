using System.Text.RegularExpressions;

namespace Blyant.Validator.Core;

/// <summary>What the checks of links share (HTML, Markdown, llms.txt): web addresses and percent-encoding.</summary>
public static partial class Urls
{
    /// <summary>True for a URL with a scheme (https:, mailto:, tel:, data:, javascript: …): not a path of a file.</summary>
    public static bool HasScheme(string value) => Scheme().IsMatch(value);

    /// <summary>A URL with its percent-encoding decoded ("caf%C3%A9" as "café"), or as it is when it cannot be.</summary>
    public static string Unescape(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text);
        }
        catch (UriFormatException)
        {
            return text;
        }
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.\-]*:")]
    private static partial Regex Scheme();
}
