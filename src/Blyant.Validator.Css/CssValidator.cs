using System.Collections.Frozen;
using System.Text;
using Blyant.Validator.Core;
using Blyant.Validator.Css.Syntax;
using Blyant.Validator.Css.Values;

namespace Blyant.Validator.Css;

/// <summary>
/// Native CSS validator: syntax (CSS Syntax Level 3), at-rules, selectors, property names and
/// property values (checked against the grammars in <c>Data/properties.txt</c>).
/// </summary>
public sealed class CssValidator : IValidator
{
    public string Engine => "native";

    public bool CanValidate(DocumentKind kind) => kind == DocumentKind.Css;

    public Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default) =>
        Task.FromResult(Validate(document.Path, document.Content));

    /// <summary>Validates a style sheet given as text (also used for &lt;style&gt; elements).</summary>
    public static IReadOnlyList<Diagnostic> Validate(string path, string css)
    {
        var run = new Run(path, css);
        run.Execute();
        return [.. run.Diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column)];
    }

    /// <summary>
    /// Validates the value of an HTML <c>style</c> attribute: declarations without a selector or braces
    /// (CSS Style Attributes, https://www.w3.org/TR/css-style-attr/)
    /// </summary>
    public static IReadOnlyList<Diagnostic> ValidateDeclarations(string path, string css)
    {
        var run = new Run(path, css);
        run.ExecuteDeclarations();
        return [.. run.Diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column)];
    }

    private enum Context
    {
        TopLevel,
        GroupRule,
        StyleRule,
        Keyframes,
        Declarations,
        FontFace,
        Page,
        Unchecked,
    }

    private static readonly StringComparer Ci = StringComparer.OrdinalIgnoreCase;

    private static readonly FrozenSet<string> KnownAtRules = new[]
    {
        "charset", "import", "namespace", "media", "supports", "layer", "container", "scope",
        "starting-style", "font-face", "keyframes", "page", "counter-style", "property",
        "font-feature-values", "font-palette-values", "view-transition", "position-try", "color-profile",
        "document",
    }.ToFrozenSet(Ci);

    /// <summary>At-rules whose block holds rules (and, inside a style rule, declarations).</summary>
    private static readonly FrozenSet<string> GroupAtRules = new[]
    {
        "media", "supports", "layer", "container", "scope", "starting-style", "document",
    }.ToFrozenSet(Ci);

    private static readonly FrozenSet<string> BlockRequired = new[]
    {
        "media", "supports", "container", "scope", "starting-style", "font-face", "keyframes", "page",
        "counter-style", "property", "font-feature-values", "font-palette-values", "position-try",
    }.ToFrozenSet(Ci);

    private static readonly FrozenSet<string> FontFaceDescriptors = new[]
    {
        "font-family", "src", "font-style", "font-weight", "font-stretch", "font-width", "font-display",
        "unicode-range", "font-feature-settings", "font-variation-settings", "ascent-override",
        "descent-override", "line-gap-override", "size-adjust", "font-named-instance",
        "font-language-override",
    }.ToFrozenSet(Ci);

    private static readonly FrozenSet<string> PageDescriptors = new[]
    {
        "size", "marks", "bleed", "page-orientation",
    }.ToFrozenSet(Ci);

    private static readonly FrozenSet<string> PageMarginRules = new[]
    {
        "top-left-corner", "top-left", "top-center", "top-right", "top-right-corner", "bottom-left-corner",
        "bottom-left", "bottom-center", "bottom-right", "bottom-right-corner", "left-top", "left-middle",
        "left-bottom", "right-top", "right-middle", "right-bottom",
    }.ToFrozenSet(Ci);

    private sealed class Run(string path, string source)
    {
        private readonly LineMap _lines = new(source);
        public List<Diagnostic> Diagnostics { get; } = [];

        private void Report(int start, int end, Severity severity, string code, string message) =>
            Diagnostics.Add(new Diagnostic(path, severity, code, message, _lines[start], _lines[end]));

        private void Report(CssToken token, Severity severity, string code, string message) =>
            Report(token.Start, token.End, severity, code, message);

        public void Execute()
        {
            var sheet = CssParser.Parse(source);
            foreach (var error in sheet.Errors)
                Report(error.Offset, error.Offset, Severity.Error, error.Code, error.Message);

            CheckTopLevelOrder(sheet.Items);
            Walk(sheet.Items, Context.TopLevel, insideStyleRule: false);
        }

        public void ExecuteDeclarations()
        {
            var sheet = CssParser.ParseDeclarationList(source);
            foreach (var error in sheet.Errors)
                Report(error.Offset, error.Offset, Severity.Error, error.Code, error.Message);

            // Only declarations: a rule or an at-rule in a style attribute is told about
            Walk(sheet.Items, Context.Declarations, insideStyleRule: false);
        }

        private void CheckTopLevelOrder(List<CssItem> items)
        {
            var index = 0;
            var seenOther = false;
            foreach (var item in items)
            {
                if (item is CssComment) continue;
                if (item is CssAtRule at)
                {
                    switch (at.Name)
                    {
                        case "charset":
                            if (index > 0 || at.Start > 0)
                                Report(at.NameToken, Severity.Error, "css.charset-position", "“@charset” must be the very first thing in the style sheet.");
                            break;
                        case "import":
                            if (seenOther)
                                Report(at.NameToken, Severity.Error, "css.import-position",
                                    "“@import” must come before all other rules (except “@charset” and “@layer” statements); this one is ignored by browsers.");
                            break;
                        case "layer" when at.Block is null:
                            break;
                        case "namespace":
                            break;
                        default:
                            seenOther = true;
                            break;
                    }
                }
                else
                {
                    seenOther = true;
                }
                index++;
            }
        }

        private void Walk(List<CssItem> items, Context context, bool insideStyleRule)
        {
            foreach (var item in items)
            {
                switch (item)
                {
                    case CssStyleRule rule:
                        WalkStyleRule(rule, context, insideStyleRule);
                        break;
                    case CssAtRule at:
                        WalkAtRule(at, context, insideStyleRule);
                        break;
                    case CssDeclaration declaration:
                        CheckDeclaration(declaration, context);
                        break;
                }
            }
        }

        private void WalkStyleRule(CssStyleRule rule, Context context, bool insideStyleRule)
        {
            switch (context)
            {
                case Context.Unchecked:
                    return;
                case Context.Keyframes:
                    CheckKeyframeSelector(rule);
                    Walk(rule.Block.Items, Context.Declarations, insideStyleRule: false);
                    return;
                case Context.Declarations or Context.FontFace or Context.Page:
                    // Nested rules are only allowed inside style rules and group rules.
                    Report(rule.Start, rule.Start, Severity.Error, "css.unexpected-rule", "A rule cannot be placed here.");
                    return;
            }

            foreach (var p in SelectorChecker.Check(rule.Prelude, source, relative: insideStyleRule))
                Report(p.At, p.Severity, p.Code, p.Message);

            Walk(rule.Block.Items, Context.StyleRule, insideStyleRule: true);
        }

        private void CheckKeyframeSelector(CssStyleRule rule)
        {
            foreach (var t in rule.Prelude)
            {
                if (t.IsTrivia || t.Type == CssTokenType.Comma) continue;
                if (t.Type == CssTokenType.Percentage && t.Number is >= 0 and <= 100) continue;
                if (t.IsIdent("from") || t.IsIdent("to")) continue;
                // Named timeline ranges (scroll-driven animations): "entry 10%".
                if (t.Type == CssTokenType.Ident && t.Value.ToLowerInvariant() is
                        "cover" or "contain" or "entry" or "exit" or "entry-crossing" or "exit-crossing" or "scroll")
                    continue;
                Report(t, Severity.Error, "css.keyframe-selector", $"“{t.Text(source)}” is not a valid keyframe selector: use “from”, “to” or a percentage.");
            }
        }

        private void WalkAtRule(CssAtRule at, Context context, bool insideStyleRule)
        {
            if (context == Context.Unchecked) return;

            var name = at.Name;
            if (context == Context.Page && PageMarginRules.Contains(name))
            {
                if (at.Block is not null) Walk(at.Block.Items, Context.Page, insideStyleRule: false);
                return;
            }

            if (CssData.IsVendorPrefixed(name))
            {
                Report(at.NameToken, Severity.Info, "css.vendor-extension", $"“@{at.NameToken.Value}” is a vendor extension.");
                if (name.EndsWith("keyframes", StringComparison.Ordinal) && at.Block is not null)
                    Walk(at.Block.Items, Context.Keyframes, insideStyleRule: false);
                return;
            }

            if (!KnownAtRules.Contains(name))
            {
                Report(at.NameToken, Severity.Warning, "css.unknown-at-rule", $"Unknown at-rule “@{at.NameToken.Value}”.");
                return;
            }

            if (at.Block is null)
            {
                if (BlockRequired.Contains(name))
                    Report(at.NameToken, Severity.Error, "css.at-rule-missing-block", $"“@{name}” needs a “{{ … }}” block.");
                return;
            }

            if (GroupAtRules.Contains(name))
            {
                Walk(at.Block.Items, insideStyleRule ? Context.StyleRule : Context.GroupRule, insideStyleRule);
                return;
            }

            switch (name)
            {
                case "keyframes":
                    Walk(at.Block.Items, Context.Keyframes, insideStyleRule: false);
                    break;
                case "font-face":
                    Walk(at.Block.Items, Context.FontFace, insideStyleRule: false);
                    break;
                case "page":
                    Walk(at.Block.Items, Context.Page, insideStyleRule: false);
                    break;
                // The descriptors of the other at-rules are not checked yet.
            }
        }

        private void CheckDeclaration(CssDeclaration declaration, Context context)
        {
            switch (context)
            {
                case Context.TopLevel or Context.GroupRule or Context.Keyframes:
                    Report(declaration.NameToken, Severity.Error, "css.unexpected-declaration",
                        $"The declaration “{declaration.Name}” must be inside a rule.");
                    return;
                case Context.Unchecked:
                    return;
                case Context.FontFace:
                    if (!FontFaceDescriptors.Contains(declaration.Name) && !declaration.IsCustomProperty)
                        Report(declaration.NameToken, Severity.Error, "css.unknown-descriptor",
                            $"“{declaration.Name}” is not a descriptor of “@font-face”.");
                    if (declaration.Value.Count == 0)
                        Report(declaration.NameToken, Severity.Error, "css.empty-value", $"“{declaration.Name}” has no value.");
                    return;
                case Context.Page when PageDescriptors.Contains(declaration.Name):
                    return;
            }

            CheckProperty(declaration);
        }

        private void CheckProperty(CssDeclaration declaration)
        {
            if (declaration.IsCustomProperty) return;

            var nameToken = declaration.NameToken;
            var name = declaration.Name.ToLowerInvariant();

            if (CssData.IsVendorPrefixed(name))
            {
                Report(nameToken, Severity.Info, "css.vendor-extension", $"“{declaration.Name}” is a vendor extension.");
                return;
            }

            if (!CssData.Properties.TryGetValue(name, out var grammar))
            {
                var suggestion = Suggest(name);
                Report(nameToken, Severity.Error, "css.unknown-property",
                    suggestion is null
                        ? $"Property “{declaration.Name}” doesn't exist."
                        : $"Property “{declaration.Name}” doesn't exist. Did you mean “{suggestion}”?");
                return;
            }

            if (declaration.Value.All(t => t.IsTrivia))
            {
                Report(nameToken, Severity.Error, "css.empty-value", $"“{declaration.Name}” has no value.");
                return;
            }

            var components = CssComponent.Build(declaration.Value);
            var all = components.SelectMany(c => c.Descendants()).ToList();

            if (all.Any(c => c.Type == CssTokenType.Function && CssData.SubstitutionFunctions.Contains(c.Token.Value)))
                return;

            var badUnit = false;
            foreach (var c in all)
            {
                if (c.Type == CssTokenType.Dimension && !CssData.IsKnownUnit(c.Token.Value))
                {
                    Report(c.Token, Severity.Error, "css.unknown-unit", $"Unknown unit “{c.Token.Value}” in “{c.Token.Text(source)}”.");
                    badUnit = true;
                }
            }
            if (badUnit) return;
            if (all.Any(c => c.Type is CssTokenType.BadString or CssTokenType.BadUrl)) return;

            if (components.Count == 1 && components[0].Type == CssTokenType.Ident &&
                CssData.CssWideKeywords.Contains(components[0].Token.Value))
                return;

            // In font lists a name like "-apple-system" is just a font name.
            var fontList = name is "font" or "font-family";
            if (!fontList && components.Any(c => c.Type is CssTokenType.Ident or CssTokenType.Function && CssData.IsVendorPrefixed(c.Token.Value)))
            {
                Report(declaration.Value[0].Start, declaration.Value[^1].End, Severity.Info, "css.vendor-extension",
                    $"The value of “{declaration.Name}” uses a vendor extension and is not checked.");
                return;
            }

            if (grammar is null) return;

            if (GrammarMatcher.Match(grammar, components) == MatchResult.Invalid)
            {
                var first = declaration.Value[0];
                var last = declaration.Value[^1];
                Report(first.Start, last.End, Severity.Error, "css.invalid-value",
                    $"Value “{Shorten(Collapse(source[first.Start..last.End]))}” is not valid for “{name}”.");
            }
        }

        private static string? Suggest(string name)
        {
            string? best = null;
            var bestDistance = 3;
            foreach (var candidate in CssData.Properties.Keys)
            {
                if (Math.Abs(candidate.Length - name.Length) >= bestDistance) continue;
                var d = Distance(name, candidate);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = candidate;
                }
            }
            return best;
        }

        // The number of edits between two names, with two letters swapped as one edit ("widht" is 1 from "width")
        internal static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (var j = 0; j <= b.Length; j++) d[0, j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
            return d[a.Length, b.Length];
        }

        private static string Collapse(string text)
        {
            var sb = new StringBuilder(text.Length);
            var space = false;
            foreach (var c in text)
            {
                if (char.IsWhiteSpace(c)) { space = true; continue; }
                if (space && sb.Length > 0) sb.Append(' ');
                space = false;
                sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Shorten(string text) => text.Length > 60 ? text[..60] + "…" : text;
    }
}
