namespace Blyant.Validator.Cli;

/// <summary>
/// The engines chosen with --engine (and :engine in the TUI): "native" (built in, for everything), "vnu" (the Nu Html
/// Checker for HTML and CSS, run with Java), "typst" (the Typst CLI for Typst files), or vnu and typst together
/// ("vnu,typst"). What an engine does not check is checked by the native engine.
/// </summary>
internal sealed record Engines(bool Vnu, bool Typst)
{
    public static Engines Native { get; } = new(false, false);

    /// <summary>The names that can be given, alone or with a comma between.</summary>
    public static IReadOnlyList<string> Names { get; } = ["native", "vnu", "typst", "vnu,typst"];

    /// <summary>The engines of a text such as "vnu,typst" (in any case, with blanks); null when it names another.</summary>
    public static Engines? Parse(string text)
    {
        var names = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(n => n.ToLowerInvariant()).ToList();
        if (names.Count == 0 || names.Any(n => n is not ("native" or "vnu" or "typst"))) return null;
        // ("native" goes with no other: it is what the others are not)
        if (names.Contains("native")) return names.All(n => n == "native") ? Native : null;
        return new Engines(names.Contains("vnu"), names.Contains("typst"));
    }

    public bool IsNative => !Vnu && !Typst;

    public override string ToString() => IsNative ? "native" : Vnu && Typst ? "vnu,typst" : Vnu ? "vnu" : "typst";
}
