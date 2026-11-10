using System.Text.RegularExpressions;

namespace Blyant.Validator.Core;

/// <summary>
/// Diagnostic codes the user has chosen to ignore. An entry is a code (<c>html.missing-lang</c>) or all the codes that
/// start with a prefix (<c>css.*</c>, <c>html.parse.*</c>). Codes are stable identifiers, so an entry keeps working
/// when a message is improved.
/// </summary>
public static partial class IgnoreList
{
    /// <summary>True if an entry is a code or a prefix ending in ".*" (and not "*" alone, which would ignore all).</summary>
    public static bool IsValid(string entry) => Entry().IsMatch(entry);

    /// <summary>The entries of a text such as "html.missing-lang, css.*" (commas or spaces between), in lower case.</summary>
    public static IReadOnlyList<string> Split(string text) =>
        [.. text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries).Select(e => e.ToLowerInvariant()).Distinct()];

    /// <summary>True if the code is ignored by one of the entries.</summary>
    public static bool Matches(string code, IEnumerable<string> entries) =>
        entries.Any(entry => entry.EndsWith(".*", StringComparison.Ordinal)
            ? code.StartsWith(entry[..^1], StringComparison.OrdinalIgnoreCase)
            : code.Equals(entry, StringComparison.OrdinalIgnoreCase));

    /// <summary>The diagnostics that are not ignored.</summary>
    public static IReadOnlyList<Diagnostic> Apply(IReadOnlyList<Diagnostic> diagnostics, IReadOnlyCollection<string> entries) =>
        entries.Count == 0 ? diagnostics : [.. diagnostics.Where(d => !Matches(d.Code, entries))];

    [GeneratedRegex(@"^[a-z0-9-]+(\.[a-z0-9-]+)*(\.\*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Entry();
}
