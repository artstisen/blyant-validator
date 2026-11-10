using System.Collections.Frozen;
using System.Reflection;

namespace Blyant.Validator.Css.Values;

/// <summary>
/// What the validator knows about CSS: property grammars and named types (from the embedded files
/// <c>Data/properties.txt</c> and <c>Data/types.txt</c>), units, colours and function names.
/// </summary>
public static class CssData
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    /// <summary>Every known property. The value is its grammar, or null when values are not checked.</summary>
    public static FrozenDictionary<string, GrammarNode?> Properties { get; }

    /// <summary>Named types defined by a grammar, e.g. <c>line-style</c>.</summary>
    public static FrozenDictionary<string, GrammarNode> Types { get; }

    static CssData()
    {
        Properties = LoadDefinitions("properties.txt", allowUnchecked: true)
            .ToFrozenDictionary(p => p.Key, p => p.Value, Ci);
        Types = LoadDefinitions("types.txt", allowUnchecked: false)
            .ToFrozenDictionary(p => p.Key.Trim('<', '>'), p => p.Value!, Ci);
    }

    /// <summary>
    /// Reads <c>name = grammar</c> lines. Lines starting with whitespace continue the line before,
    /// lines starting with <c>//</c> are comments, and a name alone is a property whose values are not checked.
    /// </summary>
    private static Dictionary<string, GrammarNode?> LoadDefinitions(string file, bool allowUnchecked)
    {
        var assembly = typeof(CssData).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + file, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        var logical = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) || line.Trim().Length == 0) continue;
            if (char.IsWhiteSpace(line[0]) && logical.Count > 0) logical[^1] += " " + line.Trim();
            else logical.Add(line.Trim());
        }

        var result = new Dictionary<string, GrammarNode?>(Ci);
        foreach (var entry in logical)
        {
            var eq = entry.IndexOf(" = ", StringComparison.Ordinal);
            if (eq < 0)
            {
                if (!allowUnchecked) throw new InvalidDataException($"{file}: missing grammar for {entry}");
                result[entry] = null;
                continue;
            }
            var name = entry[..eq].Trim();
            result[name] = GrammarParser.Parse(entry[(eq + 3)..]);
        }
        return result;
    }

    public static readonly FrozenSet<string> CssWideKeywords =
        new[] { "initial", "inherit", "unset", "revert", "revert-layer" }.ToFrozenSet(Ci);

    public static readonly FrozenSet<string> LengthUnits = new[]
    {
        "em", "rem", "ex", "rex", "cap", "rcap", "ch", "rch", "ic", "ric", "lh", "rlh",
        "vw", "vh", "vi", "vb", "vmin", "vmax",
        "svw", "svh", "svi", "svb", "svmin", "svmax",
        "lvw", "lvh", "lvi", "lvb", "lvmin", "lvmax",
        "dvw", "dvh", "dvi", "dvb", "dvmin", "dvmax",
        "cqw", "cqh", "cqi", "cqb", "cqmin", "cqmax",
        "cm", "mm", "q", "in", "pt", "pc", "px",
    }.ToFrozenSet(Ci);

    public static readonly FrozenSet<string> AngleUnits = new[] { "deg", "grad", "rad", "turn" }.ToFrozenSet(Ci);
    public static readonly FrozenSet<string> TimeUnits = new[] { "s", "ms" }.ToFrozenSet(Ci);
    public static readonly FrozenSet<string> FrequencyUnits = new[] { "hz", "khz" }.ToFrozenSet(Ci);
    public static readonly FrozenSet<string> ResolutionUnits = new[] { "dpi", "dpcm", "dppx", "x" }.ToFrozenSet(Ci);
    public static readonly FrozenSet<string> FlexUnits = new[] { "fr" }.ToFrozenSet(Ci);

    public static bool IsKnownUnit(string unit) =>
        LengthUnits.Contains(unit) || AngleUnits.Contains(unit) || TimeUnits.Contains(unit) ||
        FrequencyUnits.Contains(unit) || ResolutionUnits.Contains(unit) || FlexUnits.Contains(unit);

    /// <summary>Math functions, which can stand for any numeric value.</summary>
    public static readonly FrozenSet<string> MathFunctions = new[]
    {
        "calc", "min", "max", "clamp", "round", "mod", "rem", "sin", "cos", "tan", "asin", "acos", "atan",
        "atan2", "pow", "sqrt", "hypot", "log", "exp", "abs", "sign", "calc-size", "progress",
        "random", "sibling-index", "sibling-count",
    }.ToFrozenSet(Ci);

    /// <summary>Functions whose value is only known when the page runs: values using them are not checked.</summary>
    public static readonly FrozenSet<string> SubstitutionFunctions =
        new[] { "var", "env", "attr", "if", "inherit", "anchor", "anchor-size" }.ToFrozenSet(Ci);

    public static readonly FrozenSet<string> ColorFunctions = new[]
    {
        "rgb", "rgba", "hsl", "hsla", "hwb", "lab", "lch", "oklab", "oklch", "color", "color-mix",
        "light-dark", "contrast-color", "device-cmyk",
    }.ToFrozenSet(Ci);

    public static readonly FrozenSet<string> ImageFunctions = new[]
    {
        "linear-gradient", "radial-gradient", "conic-gradient", "repeating-linear-gradient",
        "repeating-radial-gradient", "repeating-conic-gradient", "image", "image-set", "cross-fade",
        "element", "paint", "light-dark",
    }.ToFrozenSet(Ci);

    /// <summary>Types that are matched by function name; the arguments are not checked.</summary>
    public static readonly FrozenDictionary<string, FrozenSet<string>> FunctionTypes =
        new Dictionary<string, FrozenSet<string>>(Ci)
        {
            ["transform-function"] = new[]
            {
                "matrix", "matrix3d", "translate", "translate3d", "translatex", "translatey", "translatez",
                "scale", "scale3d", "scalex", "scaley", "scalez", "rotate", "rotate3d", "rotatex", "rotatey",
                "rotatez", "skew", "skewx", "skewy", "perspective",
            }.ToFrozenSet(Ci),
            ["filter-function"] = new[]
            {
                "blur", "brightness", "contrast", "drop-shadow", "grayscale", "hue-rotate", "invert",
                "opacity", "saturate", "sepia",
            }.ToFrozenSet(Ci),
            ["easing-function-call"] = new[] { "linear", "cubic-bezier", "steps" }.ToFrozenSet(Ci),
            ["basic-shape"] = new[]
            {
                "inset", "circle", "ellipse", "polygon", "path", "rect", "xywh", "shape",
            }.ToFrozenSet(Ci),
            ["counter-function"] = new[] { "counter", "counters" }.ToFrozenSet(Ci),
            ["symbols-function"] = new[] { "symbols" }.ToFrozenSet(Ci),
            ["grid-function"] = new[] { "repeat", "minmax", "fit-content" }.ToFrozenSet(Ci),
            // Generated content: target-counter(), leader() … (and content() of paged media)
            ["target-function"] = new[]
            {
                "target-counter", "target-counters", "target-text", "leader", "content", "string",
            }.ToFrozenSet(Ci),
            // Scroll-driven animations: scroll() and view()
            ["timeline-function"] = new[] { "scroll", "view" }.ToFrozenSet(Ci),
            ["palette-mix-function"] = new[] { "palette-mix" }.ToFrozenSet(Ci),
            ["path-function"] = new[] { "path", "shape" }.ToFrozenSet(Ci),
        }.ToFrozenDictionary(Ci);

    public static readonly FrozenSet<string> NamedColors = new[]
    {
        "aliceblue", "antiquewhite", "aqua", "aquamarine", "azure", "beige", "bisque", "black",
        "blanchedalmond", "blue", "blueviolet", "brown", "burlywood", "cadetblue", "chartreuse", "chocolate",
        "coral", "cornflowerblue", "cornsilk", "crimson", "cyan", "darkblue", "darkcyan", "darkgoldenrod",
        "darkgray", "darkgreen", "darkgrey", "darkkhaki", "darkmagenta", "darkolivegreen", "darkorange",
        "darkorchid", "darkred", "darksalmon", "darkseagreen", "darkslateblue", "darkslategray",
        "darkslategrey", "darkturquoise", "darkviolet", "deeppink", "deepskyblue", "dimgray", "dimgrey",
        "dodgerblue", "firebrick", "floralwhite", "forestgreen", "fuchsia", "gainsboro", "ghostwhite", "gold",
        "goldenrod", "gray", "green", "greenyellow", "grey", "honeydew", "hotpink", "indianred", "indigo",
        "ivory", "khaki", "lavender", "lavenderblush", "lawngreen", "lemonchiffon", "lightblue", "lightcoral",
        "lightcyan", "lightgoldenrodyellow", "lightgray", "lightgreen", "lightgrey", "lightpink",
        "lightsalmon", "lightseagreen", "lightskyblue", "lightslategray", "lightslategrey", "lightsteelblue",
        "lightyellow", "lime", "limegreen", "linen", "magenta", "maroon", "mediumaquamarine", "mediumblue",
        "mediumorchid", "mediumpurple", "mediumseagreen", "mediumslateblue", "mediumspringgreen",
        "mediumturquoise", "mediumvioletred", "midnightblue", "mintcream", "mistyrose", "moccasin",
        "navajowhite", "navy", "oldlace", "olive", "olivedrab", "orange", "orangered", "orchid",
        "palegoldenrod", "palegreen", "paleturquoise", "palevioletred", "papayawhip", "peachpuff", "peru",
        "pink", "plum", "powderblue", "purple", "rebeccapurple", "red", "rosybrown", "royalblue",
        "saddlebrown", "salmon", "sandybrown", "seagreen", "seashell", "sienna", "silver", "skyblue",
        "slateblue", "slategray", "slategrey", "snow", "springgreen", "steelblue", "tan", "teal", "thistle",
        "tomato", "turquoise", "violet", "wheat", "white", "whitesmoke", "yellow", "yellowgreen",
        "transparent", "currentcolor",
        // System colours (CSS Color 4), including the deprecated ones browsers still accept.
        "accentcolor", "accentcolortext", "activetext", "buttonborder", "buttonface", "buttontext", "canvas",
        "canvastext", "field", "fieldtext", "graytext", "highlight", "highlighttext", "linktext", "mark",
        "marktext", "selecteditem", "selecteditemtext", "visitedtext", "activeborder", "activecaption",
        "appworkspace", "background", "buttonhighlight", "buttonshadow", "captiontext", "inactiveborder",
        "inactivecaption", "inactivecaptiontext", "infobackground", "infotext", "menu", "menutext",
        "scrollbar", "threeddarkshadow", "threedface", "threedhighlight", "threedlightshadow",
        "threedshadow", "window", "windowframe", "windowtext",
    }.ToFrozenSet(Ci);

    public static bool IsVendorPrefixed(string name) =>
        name.Length > 1 && name[0] == '-' && name[1] != '-' &&
        (name.StartsWith("-webkit-", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("-moz-", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("-ms-", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("-o-", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("-khtml-", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("-apple-", StringComparison.OrdinalIgnoreCase) ||
         name.StartsWith("-epub-", StringComparison.OrdinalIgnoreCase));

    /// <summary>The names of types the matcher knows without a grammar.</summary>
    public static readonly FrozenSet<string> BuiltInTypes = new[]
    {
        "length", "percentage", "length-percentage", "number", "integer", "angle", "angle-percentage",
        "time", "time-percentage", "frequency", "resolution", "flex", "string", "url", "custom-ident",
        "dashed-ident", "ident", "hex-color", "color", "named-color", "image", "any-value", "line-names",
    }.ToFrozenSet(Ci);
}
