using System.Globalization;
using System.Text;

namespace Blyant.Validator.Css.Values;

/// <summary>
/// A parsed value definition, in the notation of the CSS specifications
/// (https://www.w3.org/TR/css-values-4/#value-defs).
/// </summary>
public abstract record GrammarNode;

/// <summary>A keyword such as <c>auto</c>, compared without regard to case.</summary>
public sealed record KeywordNode(string Keyword) : GrammarNode;

/// <summary>A literal <c>,</c> or <c>/</c>.</summary>
public sealed record LiteralNode(char Character) : GrammarNode;

/// <summary><c>&lt;length [0,∞]&gt;</c>: a data type, optionally with a range.</summary>
public sealed record TypeNode(string Name, double? Min = null, double? Max = null) : GrammarNode;

/// <summary><c>&lt;'margin-top'&gt;</c>: the grammar of another property.</summary>
public sealed record PropertyNode(string Property) : GrammarNode;

/// <summary><c>fit-content( &lt;length-percentage&gt; )</c>.</summary>
public sealed record FunctionNode(string Name, GrammarNode? Arguments) : GrammarNode;

/// <summary>Components written one after the other: all must match, in order.</summary>
public sealed record SequenceNode(IReadOnlyList<GrammarNode> Items) : GrammarNode;

/// <summary><c>a &amp;&amp; b</c>: all must match, in any order.</summary>
public sealed record AllOfNode(IReadOnlyList<GrammarNode> Items) : GrammarNode;

/// <summary><c>a || b</c>: one or more must match, in any order.</summary>
public sealed record AnyOfNode(IReadOnlyList<GrammarNode> Items) : GrammarNode;

/// <summary><c>a | b</c>: exactly one must match.</summary>
public sealed record OneOfNode(IReadOnlyList<GrammarNode> Items) : GrammarNode;

/// <summary><c>?</c>, <c>*</c>, <c>+</c>, <c>{m,n}</c> and <c>#</c> (comma-separated).</summary>
public sealed record RepeatNode(GrammarNode Item, int Min, int Max, bool CommaSeparated) : GrammarNode;

/// <summary>Parses value definitions such as <c>[ &lt;length&gt; | auto ]{1,4}</c>.</summary>
public static class GrammarParser
{
    public static GrammarNode Parse(string text)
    {
        var tokens = Lex(text);
        var index = 0;
        var node = ParseOneOf(tokens, ref index);
        if (index != tokens.Count)
            throw new System.FormatException($"Unexpected “{tokens[index]}” in grammar: {text}");
        return node;
    }

    private static List<string> Lex(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (c == '<')
            {
                var end = text.IndexOf('>', i);
                if (end < 0) throw new System.FormatException($"Unclosed “<” in grammar: {text}");
                tokens.Add(text[i..(end + 1)]);
                i = end + 1;
                continue;
            }
            if (c == '{')
            {
                var end = text.IndexOf('}', i);
                tokens.Add(text[i..(end + 1)]);
                i = end + 1;
                continue;
            }
            if ((c == '&' || c == '|') && i + 1 < text.Length && text[i + 1] == c)
            {
                tokens.Add(new string(c, 2));
                i += 2;
                continue;
            }
            if ("[]|?*+#!,/)".Contains(c))
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }
            if (char.IsLetter(c) || c == '-' || char.IsDigit(c))
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '-')) i++;
                if (i < text.Length && text[i] == '(')
                {
                    i++;
                    tokens.Add(text[start..i]);
                }
                else
                {
                    tokens.Add(text[start..i]);
                }
                continue;
            }
            throw new System.FormatException($"Unexpected “{c}” in grammar: {text}");
        }
        return tokens;
    }

    private static bool IsOperator(string t) => t is "|" or "||" or "&&" or "]" or ")";

    private static GrammarNode ParseOneOf(List<string> t, ref int i)
    {
        var items = new List<GrammarNode> { ParseAnyOf(t, ref i) };
        while (i < t.Count && t[i] == "|") { i++; items.Add(ParseAnyOf(t, ref i)); }
        return items.Count == 1 ? items[0] : new OneOfNode(items);
    }

    private static GrammarNode ParseAnyOf(List<string> t, ref int i)
    {
        var items = new List<GrammarNode> { ParseAllOf(t, ref i) };
        while (i < t.Count && t[i] == "||") { i++; items.Add(ParseAllOf(t, ref i)); }
        return items.Count == 1 ? items[0] : new AnyOfNode(items);
    }

    private static GrammarNode ParseAllOf(List<string> t, ref int i)
    {
        var items = new List<GrammarNode> { ParseSequence(t, ref i) };
        while (i < t.Count && t[i] == "&&") { i++; items.Add(ParseSequence(t, ref i)); }
        return items.Count == 1 ? items[0] : new AllOfNode(items);
    }

    private static GrammarNode ParseSequence(List<string> t, ref int i)
    {
        var items = new List<GrammarNode>();
        while (i < t.Count && !IsOperator(t[i]))
            items.Add(ParseRepeated(t, ref i));
        if (items.Count == 0) throw new System.FormatException("Empty sequence in grammar.");
        return items.Count == 1 ? items[0] : new SequenceNode(items);
    }

    private static GrammarNode ParseRepeated(List<string> t, ref int i)
    {
        var node = ParsePrimary(t, ref i);
        while (i < t.Count)
        {
            var m = t[i];
            if (m == "?") node = new RepeatNode(node, 0, 1, false);
            else if (m == "*") node = new RepeatNode(node, 0, int.MaxValue, false);
            else if (m == "+") node = new RepeatNode(node, 1, int.MaxValue, false);
            else if (m == "!") { /* "must not be empty": the groups it is used on cannot be empty here */ }
            else if (m == "#")
            {
                if (i + 1 < t.Count && t[i + 1].StartsWith('{'))
                {
                    i++;
                    var (min, max) = ParseRange(t[i]);
                    node = new RepeatNode(node, min, max, true);
                }
                else
                {
                    node = new RepeatNode(node, 1, int.MaxValue, true);
                }
            }
            else if (m.StartsWith('{'))
            {
                var (min, max) = ParseRange(m);
                node = new RepeatNode(node, min, max, false);
            }
            else break;
            i++;
        }
        return node;
    }

    private static (int Min, int Max) ParseRange(string braces)
    {
        var inner = braces[1..^1];
        var comma = inner.IndexOf(',');
        if (comma < 0)
        {
            var n = int.Parse(inner, CultureInfo.InvariantCulture);
            return (n, n);
        }
        var min = int.Parse(inner[..comma], CultureInfo.InvariantCulture);
        var maxText = inner[(comma + 1)..].Trim();
        return (min, maxText.Length == 0 ? int.MaxValue : int.Parse(maxText, CultureInfo.InvariantCulture));
    }

    private static GrammarNode ParsePrimary(List<string> t, ref int i)
    {
        var token = t[i++];
        if (token == "[")
        {
            var inner = ParseOneOf(t, ref i);
            if (i >= t.Count || t[i] != "]") throw new System.FormatException("Missing “]” in grammar.");
            i++;
            return inner;
        }
        if (token == ",") return new LiteralNode(',');
        if (token == "/") return new LiteralNode('/');
        if (token.StartsWith("<'", StringComparison.Ordinal)) return new PropertyNode(token[2..^2]);
        if (token.StartsWith('<')) return ParseType(token);
        if (token.EndsWith('('))
        {
            var name = token[..^1];
            GrammarNode? args = null;
            if (i < t.Count && t[i] != ")") args = ParseOneOf(t, ref i);
            if (i >= t.Count || t[i] != ")") throw new System.FormatException($"Missing “)” after {name}( in grammar.");
            i++;
            return new FunctionNode(name, args);
        }
        return new KeywordNode(token);
    }

    private static TypeNode ParseType(string token)
    {
        var inner = token[1..^1].Trim();
        var bracket = inner.IndexOf('[');
        if (bracket < 0) return new TypeNode(inner);

        var name = inner[..bracket].Trim();
        var range = inner[(bracket + 1)..inner.IndexOf(']')].Split(',');
        return new TypeNode(name, ParseBound(range[0]), ParseBound(range[1]));
    }

    private static double? ParseBound(string text)
    {
        text = text.Trim();
        if (text is "∞" or "+∞" or "-∞") return null;
        var digits = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsDigit(c) || c is '-' or '+' or '.') digits.Append(c);
            else break;
        }
        return double.Parse(digits.ToString(), CultureInfo.InvariantCulture);
    }
}
