namespace Blyant.Validator.Core;

/// <summary>
/// Projects Blyant Validator is modelled on or built with. Shown by <c>blyant-validator credits</c>
/// and kept in sync with CREDITS.md and THIRD-PARTY-NOTICES.md.
/// </summary>
public static class Credits
{
    public sealed record Entry(string Name, string Role, string Url, string License);

    public static IReadOnlyList<Entry> Inspiration { get; } =
    [
        new("The Nu Html Checker (v.Nu)",
            "Reference for HTML conformance checking; optional --engine vnu backend",
            "https://github.com/validator/validator",
            "MIT"),
        new("W3C CSS Validation Service",
            "Reference for CSS validation behaviour and messages",
            "https://github.com/w3c/css-validator",
            "W3C Software and Document Notice and License"),
    ];
}
