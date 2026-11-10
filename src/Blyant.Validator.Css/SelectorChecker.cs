using System.Collections.Frozen;
using Blyant.Validator.Core;
using Blyant.Validator.Css.Syntax;
using Blyant.Validator.Css.Values;

namespace Blyant.Validator.Css;

/// <summary>A problem found in a selector.</summary>
internal sealed record SelectorProblem(CssToken At, Severity Severity, string Code, string Message);

/// <summary>
/// Checks the structure of selectors (Selectors Level 4): combinators, class and ID names, attribute
/// selectors and the names of pseudo-classes and pseudo-elements. The arguments of functional
/// pseudo-classes such as <c>:is()</c> are not checked.
/// </summary>
internal static class SelectorChecker
{
    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static readonly FrozenSet<string> PseudoClasses = new[]
    {
        "active", "active-view-transition", "active-view-transition-type", "any-link", "autofill", "blank",
        "buffering", "checked", "closed", "current", "default", "defined", "dir", "disabled", "empty",
        "enabled", "first", "first-child", "first-of-type", "focus", "focus-visible", "focus-within",
        "fullscreen", "future", "has", "has-slotted", "heading", "host", "host-context", "hover",
        "in-range", "indeterminate", "invalid", "is", "lang", "last-child", "last-of-type", "left", "link",
        "local-link", "modal", "muted", "not", "nth-child", "nth-col", "nth-last-child", "nth-last-col",
        "nth-last-of-type", "nth-of-type", "only-child", "only-of-type", "open", "optional",
        "out-of-range", "past", "paused", "picture-in-picture", "placeholder-shown", "playing",
        "popover-open", "read-only", "read-write", "required", "right", "root", "scope", "seeking",
        "stalled", "state", "target", "target-current", "target-within", "user-invalid", "user-valid",
        "valid", "visited", "volume-locked", "where",
        // Pseudo-elements that may be written with one colon (CSS 2).
        "before", "after", "first-line", "first-letter",
    }.ToFrozenSet(Ci);

    private static readonly FrozenSet<string> PseudoElements = new[]
    {
        "after", "backdrop", "before", "checkmark", "column", "cue", "cue-region", "details-content",
        "file-selector-button", "first-letter", "first-line", "grammar-error", "highlight", "marker",
        "part", "picker", "picker-icon", "placeholder", "scroll-button", "scroll-marker",
        "scroll-marker-group", "search-text", "selection", "slotted", "spelling-error", "target-text",
        "view-transition", "view-transition-group", "view-transition-image-pair", "view-transition-new",
        "view-transition-old",
    }.ToFrozenSet(Ci);

    /// <param name="prelude">The selector tokens, with whitespace.</param>
    /// <param name="relative">True for nested rules, which may start with a combinator.</param>
    public static List<SelectorProblem> Check(IReadOnlyList<CssToken> prelude, string source, bool relative)
    {
        var problems = new List<SelectorProblem>();
        var parts = SplitAtCommas(prelude);
        foreach (var part in parts) CheckComplex(part, source, relative, problems);
        return problems;
    }

    private static List<List<CssToken>> SplitAtCommas(IReadOnlyList<CssToken> tokens)
    {
        var parts = new List<List<CssToken>> { new() };
        var depth = 0;
        foreach (var t in tokens)
        {
            if (t.Type is CssTokenType.Function or CssTokenType.LeftParen or CssTokenType.LeftSquare) depth++;
            else if (t.Type is CssTokenType.RightParen or CssTokenType.RightSquare && depth > 0) depth--;

            if (t.Type == CssTokenType.Comma && depth == 0) parts.Add([]);
            else parts[^1].Add(t);
        }
        return parts;
    }

    private static void CheckComplex(List<CssToken> tokens, string source, bool relative, List<SelectorProblem> problems)
    {
        var meaningful = tokens.Where(t => !t.IsTrivia).ToList();
        if (meaningful.Count == 0)
        {
            var at = tokens.Count > 0 ? tokens[0] : null;
            if (at is not null)
                problems.Add(new(at, Severity.Error, "css.selector.empty", "Empty selector: there is nothing between two commas, or before or after one."));
            else
                problems.Add(new(new CssToken(CssTokenType.Whitespace, 0, 0, ""), Severity.Error, "css.selector.empty", "Empty selector."));
            return;
        }

        var lastWasCombinator = false;
        var sawCompound = false;
        CssToken? lastCombinator = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            switch (t.Type)
            {
                case CssTokenType.Whitespace or CssTokenType.Comment:
                    continue;

                case CssTokenType.Delim when t.Value is ">" or "+" or "~":
                    if (lastWasCombinator)
                        problems.Add(new(t, Severity.Error, "css.selector.double-combinator", $"Two combinators in a row before “{t.Value}”."));
                    else if (!sawCompound && !relative)
                        problems.Add(new(t, Severity.Error, "css.selector.leading-combinator", $"Selector starts with the combinator “{t.Value}”."));
                    lastWasCombinator = true;
                    lastCombinator = t;
                    continue;

                case CssTokenType.Ident:
                case CssTokenType.Delim when t.Value is "*" or "&" or "|":
                    break;

                case CssTokenType.Delim when t.Value == ".":
                    if (i + 1 >= tokens.Count || tokens[i + 1].Type != CssTokenType.Ident)
                        problems.Add(new(t, Severity.Error, "css.selector.bad-class", "Expected a class name after “.”."));
                    else i++;
                    break;

                case CssTokenType.Hash:
                    if (!t.IsIdHash)
                        problems.Add(new(t, Severity.Error, "css.selector.bad-id",
                            $"“#{t.Value}” is not a valid ID selector: an ID in a selector cannot start with a digit. Escape it, e.g. “#\\3{t.Value[0]} {t.Value[1..]}”."));
                    break;

                case CssTokenType.Colon:
                    i = CheckPseudo(tokens, i, problems);
                    break;

                case CssTokenType.LeftSquare:
                    i = CheckAttribute(tokens, i, problems);
                    break;

                default:
                    problems.Add(new(t, Severity.Error, "css.selector.unexpected", $"Unexpected “{t.Text(source)}” in selector."));
                    i = SkipBlock(tokens, i);
                    break;
            }
            lastWasCombinator = false;
            sawCompound = true;
        }

        if (lastWasCombinator && lastCombinator is not null)
            problems.Add(new(lastCombinator, Severity.Error, "css.selector.trailing-combinator", $"Selector ends with the combinator “{lastCombinator.Value}”."));
    }

    private static int CheckPseudo(List<CssToken> tokens, int i, List<SelectorProblem> problems)
    {
        var colon = tokens[i];
        var element = i + 1 < tokens.Count && tokens[i + 1].Type == CssTokenType.Colon;
        var nameIndex = i + (element ? 2 : 1);
        if (nameIndex >= tokens.Count || tokens[nameIndex].Type is not (CssTokenType.Ident or CssTokenType.Function))
        {
            problems.Add(new(colon, Severity.Error, "css.selector.bad-pseudo", $"Expected a name after “{(element ? "::" : ":")}”."));
            return nameIndex - 1;
        }

        var nameToken = tokens[nameIndex];
        var name = nameToken.Value;
        var prefix = element ? "::" : ":";
        var display = prefix + name + (nameToken.Type == CssTokenType.Function ? "()" : "");

        if (CssData.IsVendorPrefixed(name))
            problems.Add(new(nameToken, Severity.Info, "css.vendor-extension", $"“{display}” is a vendor extension."));
        else if (element && !PseudoElements.Contains(name))
            problems.Add(new(nameToken, Severity.Error, "css.selector.unknown-pseudo-element", $"Unknown pseudo-element “{display}”."));
        else if (!element && (WebKitScrollbarStates.Contains(name) && AfterVendorPseudoElement(tokens, i) || name.Equals("window-inactive", StringComparison.OrdinalIgnoreCase)))
            problems.Add(new(nameToken, Severity.Info, "css.vendor-extension", $"“{display}” is a vendor extension."));
        else if (!element && !PseudoClasses.Contains(name))
            problems.Add(new(nameToken, Severity.Error, "css.selector.unknown-pseudo-class",
                PseudoElements.Contains(name)
                    ? $"Unknown pseudo-class “{display}”. Did you mean the pseudo-element “::{name}”?"
                    : $"Unknown pseudo-class “{display}”."));

        return nameToken.Type == CssTokenType.Function ? SkipBlock(tokens, nameIndex) : nameIndex;
    }

    /// <summary>The pseudo-classes of WebKit's scrollbar pseudo-elements (::-webkit-scrollbar-thumb:horizontal …).</summary>
    private static readonly HashSet<string> WebKitScrollbarStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "horizontal", "vertical", "decrement", "increment", "start", "end", "double-button", "single-button",
        "no-button", "corner-present",
    };

    // True when the compound selector before the pseudo-class at i has a vendor pseudo-element (::-webkit-…)
    private static bool AfterVendorPseudoElement(List<CssToken> tokens, int i)
    {
        for (var k = i - 1; k >= 1; k--)
        {
            var t = tokens[k];
            if (t.Type is CssTokenType.Whitespace or CssTokenType.Comma || t.IsDelim('>') || t.IsDelim('+') || t.IsDelim('~'))
                return false;
            if (t.Type == CssTokenType.Ident && CssData.IsVendorPrefixed(t.Value) &&
                tokens[k - 1].Type == CssTokenType.Colon && k >= 2 && tokens[k - 2].Type == CssTokenType.Colon)
                return true;
        }

        return false;
    }

    private static int CheckAttribute(List<CssToken> tokens, int i, List<SelectorProblem> problems)
    {
        var open = tokens[i];
        var k = i + 1;
        while (k < tokens.Count && tokens[k].IsTrivia) k++;
        if (k >= tokens.Count || !(tokens[k].Type == CssTokenType.Ident || tokens[k].IsDelim('*') || tokens[k].IsDelim('|')))
            problems.Add(new(open, Severity.Error, "css.selector.bad-attribute", "Expected an attribute name after “[”."));
        return SkipBlock(tokens, i);
    }

    /// <summary>Returns the index of the token that closes the block opened at <paramref name="i"/>.</summary>
    private static int SkipBlock(List<CssToken> tokens, int i)
    {
        if (tokens[i].Type is not (CssTokenType.Function or CssTokenType.LeftParen or CssTokenType.LeftSquare)) return i;
        var depth = 0;
        for (var k = i; k < tokens.Count; k++)
        {
            if (tokens[k].Type is CssTokenType.Function or CssTokenType.LeftParen or CssTokenType.LeftSquare) depth++;
            else if (tokens[k].Type is CssTokenType.RightParen or CssTokenType.RightSquare && --depth == 0) return k;
        }
        return tokens.Count - 1;
    }
}
