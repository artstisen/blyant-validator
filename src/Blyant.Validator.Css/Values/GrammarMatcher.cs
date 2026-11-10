using Blyant.Validator.Css.Syntax;

namespace Blyant.Validator.Css.Values;

/// <summary>The outcome of matching a value against a grammar.</summary>
public enum MatchResult
{
    Valid,
    Invalid,

    /// <summary>Too many ways to try: the value is accepted rather than risk a false error.</summary>
    Undecided,
}

/// <summary>
/// Matches component values against a <see cref="GrammarNode"/>. Every node returns the set of
/// positions where a match could end, so all ways of matching are tried (backtracking).
/// </summary>
public sealed class GrammarMatcher
{
    private const int StepBudget = 200_000;
    private int _steps;
    private bool _gaveUp;

    public static MatchResult Match(GrammarNode grammar, IReadOnlyList<CssComponent> value)
    {
        var matcher = new GrammarMatcher();
        var ends = matcher.MatchNode(grammar, value, 0);
        if (ends.Contains(value.Count)) return MatchResult.Valid;
        return matcher._gaveUp ? MatchResult.Undecided : MatchResult.Invalid;
    }

    private HashSet<int> MatchNode(GrammarNode node, IReadOnlyList<CssComponent> input, int pos)
    {
        if (++_steps > StepBudget)
        {
            _gaveUp = true;
            return [];
        }

        switch (node)
        {
            case KeywordNode k:
                return pos < input.Count && input[pos].Token.IsIdent(k.Keyword) ? [pos + 1] : [];

            case LiteralNode l:
                if (pos >= input.Count) return [];
                var t = input[pos].Token;
                var ok = l.Character == ',' ? t.Type == CssTokenType.Comma : t.IsDelim(l.Character);
                return ok ? [pos + 1] : [];

            case TypeNode type:
                return MatchType(type, input, pos);

            case PropertyNode p:
                if (CssData.Properties.TryGetValue(p.Property, out var grammar) && grammar is not null)
                    return MatchNode(grammar, input, pos);
                // A property without a grammar: accept a single component rather than guess.
                return pos < input.Count ? [pos + 1] : [];

            case FunctionNode f:
                if (pos >= input.Count) return [];
                var fn = input[pos];
                if (fn.Type != CssTokenType.Function || !fn.Token.Value.Equals(f.Name, StringComparison.OrdinalIgnoreCase))
                    return [];
                var args = fn.Children ?? [];
                if (f.Arguments is null) return args.Count == 0 ? [pos + 1] : [];
                return MatchNode(f.Arguments, args, 0).Contains(args.Count) ? [pos + 1] : [];

            case SequenceNode s:
                HashSet<int> current = [pos];
                foreach (var item in s.Items)
                {
                    var next = new HashSet<int>();
                    foreach (var p in current) next.UnionWith(MatchNode(item, input, p));
                    if (next.Count == 0) return next;
                    current = next;
                }
                return current;

            case OneOfNode o:
                var any = new HashSet<int>();
                foreach (var item in o.Items) any.UnionWith(MatchNode(item, input, pos));
                return any;

            case AnyOfNode a:
                return MatchUnordered(a.Items, input, pos, requireAll: false);

            case AllOfNode a:
                return MatchUnordered(a.Items, input, pos, requireAll: true);

            case RepeatNode r:
                return MatchRepeat(r, input, pos);

            default:
                throw new InvalidOperationException($"Unknown grammar node {node.GetType().Name}");
        }
    }

    /// <summary><c>||</c> and <c>&amp;&amp;</c>: each item at most once, in any order.</summary>
    private HashSet<int> MatchUnordered(IReadOnlyList<GrammarNode> items, IReadOnlyList<CssComponent> input, int pos, bool requireAll)
    {
        var full = (1 << items.Count) - 1;
        var result = new HashSet<int>();
        var seen = new HashSet<(int Pos, int Mask)> { (pos, 0) };
        var queue = new Queue<(int Pos, int Mask)>();
        queue.Enqueue((pos, 0));

        while (queue.Count > 0)
        {
            var (p, mask) = queue.Dequeue();
            if (mask != 0 && (!requireAll || mask == full)) result.Add(p);
            for (var i = 0; i < items.Count; i++)
            {
                if ((mask & (1 << i)) != 0) continue;
                foreach (var end in MatchNode(items[i], input, p))
                {
                    var state = (end, mask | (1 << i));
                    if (seen.Add(state)) queue.Enqueue(state);
                }
                if (_gaveUp) return result;
            }
        }
        return result;
    }

    private HashSet<int> MatchRepeat(RepeatNode r, IReadOnlyList<CssComponent> input, int pos)
    {
        var result = new HashSet<int>();
        var seen = new HashSet<(int Pos, int Count)> { (pos, 0) };
        var queue = new Queue<(int Pos, int Count)>();
        queue.Enqueue((pos, 0));

        while (queue.Count > 0)
        {
            var (p, count) = queue.Dequeue();
            if (count >= r.Min) result.Add(p);
            if (count >= r.Max) continue;

            var start = p;
            if (r.CommaSeparated && count > 0)
            {
                if (start >= input.Count || input[start].Type != CssTokenType.Comma) continue;
                start++;
            }

            foreach (var end in MatchNode(r.Item, input, start))
            {
                // An empty match only helps to reach the minimum.
                if (end == p && count >= r.Min) continue;
                var state = (end, count + 1);
                if (seen.Add(state)) queue.Enqueue(state);
            }
            if (_gaveUp) return result;
        }
        return result;
    }

    private HashSet<int> MatchType(TypeNode type, IReadOnlyList<CssComponent> input, int pos)
    {
        var name = type.Name;

        if (CssData.Types.TryGetValue(name, out var grammar))
            return MatchNode(grammar, input, pos);

        if (CssData.FunctionTypes.TryGetValue(name, out var functions))
        {
            if (pos < input.Count && input[pos].Type == CssTokenType.Function &&
                (functions.Contains(input[pos].Token.Value) || CssData.IsVendorPrefixed(input[pos].Token.Value)))
                return [pos + 1];
            return [];
        }

        if (pos >= input.Count) return [];
        return MatchesBuiltIn(name, input[pos], type.Min, type.Max) ? [pos + 1] : [];
    }

    private static bool MatchesBuiltIn(string name, CssComponent c, double? min, double? max)
    {
        var t = c.Token;

        // A math function can stand for any number-like value.
        if (t.Type == CssTokenType.Function && CssData.MathFunctions.Contains(t.Value) && IsNumericType(name))
            return true;

        bool InRange(double n) => (min is null || n >= min) && (max is null || n <= max);

        switch (name.ToLowerInvariant())
        {
            case "number":
                return t.Type == CssTokenType.Number && InRange(t.Number);
            case "integer":
                return t.Type == CssTokenType.Number && t.IsInteger && InRange(t.Number);
            case "percentage":
                return t.Type == CssTokenType.Percentage && InRange(t.Number);
            case "length":
                return IsLength(t) && InRange(t.Number);
            case "length-percentage":
                return (IsLength(t) || t.Type == CssTokenType.Percentage) && InRange(t.Number);
            case "angle":
                return IsDimension(t, CssData.AngleUnits, zeroAllowed: true) && InRange(t.Number);
            case "angle-percentage":
                return (IsDimension(t, CssData.AngleUnits, zeroAllowed: true) || t.Type == CssTokenType.Percentage) && InRange(t.Number);
            case "time":
                return IsDimension(t, CssData.TimeUnits, zeroAllowed: false) && InRange(t.Number);
            case "time-percentage":
                return (IsDimension(t, CssData.TimeUnits, zeroAllowed: false) || t.Type == CssTokenType.Percentage) && InRange(t.Number);
            case "frequency":
                return IsDimension(t, CssData.FrequencyUnits, zeroAllowed: false) && InRange(t.Number);
            case "resolution":
                return IsDimension(t, CssData.ResolutionUnits, zeroAllowed: false) && InRange(t.Number);
            case "flex":
                return IsDimension(t, CssData.FlexUnits, zeroAllowed: false) && InRange(t.Number);
            case "string":
                return t.Type == CssTokenType.String;
            case "url":
                return IsUrl(c);
            case "ident":
                return t.Type == CssTokenType.Ident;
            case "custom-ident":
                return t.Type == CssTokenType.Ident && !CssData.CssWideKeywords.Contains(t.Value) &&
                       !t.Value.Equals("default", StringComparison.OrdinalIgnoreCase);
            case "dashed-ident":
                return t.Type == CssTokenType.Ident && t.Value.StartsWith("--", StringComparison.Ordinal);
            case "hex-color":
                return IsHexColor(t);
            case "named-color":
                return t.Type == CssTokenType.Ident && CssData.NamedColors.Contains(t.Value);
            case "color":
                return IsHexColor(t) ||
                       (t.Type == CssTokenType.Ident && CssData.NamedColors.Contains(t.Value)) ||
                       (t.Type == CssTokenType.Function && (CssData.ColorFunctions.Contains(t.Value) || CssData.IsVendorPrefixed(t.Value)));
            case "image":
                return IsUrl(c) ||
                       (t.Type == CssTokenType.Function && (CssData.ImageFunctions.Contains(t.Value) || CssData.IsVendorPrefixed(t.Value)));
            case "any-value":
                return true;
            default:
                // A type without a definition: accept a single component rather than report a false error.
                return true;
        }
    }

    private static bool IsNumericType(string name) => name.ToLowerInvariant() is
        "number" or "integer" or "percentage" or "length" or "length-percentage" or "angle" or
        "angle-percentage" or "time" or "time-percentage" or "frequency" or "resolution" or "flex";

    private static bool IsLength(CssToken t) =>
        (t.Type == CssTokenType.Dimension && CssData.LengthUnits.Contains(t.Value)) ||
        (t.Type == CssTokenType.Number && t.Number == 0);

    private static bool IsDimension(CssToken t, IReadOnlySet<string> units, bool zeroAllowed) =>
        (t.Type == CssTokenType.Dimension && units.Contains(t.Value)) ||
        (zeroAllowed && t.Type == CssTokenType.Number && t.Number == 0);

    private static bool IsUrl(CssComponent c) =>
        c.Type == CssTokenType.Url ||
        (c.Type == CssTokenType.Function &&
         (c.Token.Value.Equals("url", StringComparison.OrdinalIgnoreCase) || c.Token.Value.Equals("src", StringComparison.OrdinalIgnoreCase)));

    private static bool IsHexColor(CssToken t) =>
        t.Type == CssTokenType.Hash && t.Value.Length is 3 or 4 or 6 or 8 && t.Value.All(char.IsAsciiHexDigit);
}
