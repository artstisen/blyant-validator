using Blyant.Validator.Css.Syntax;

namespace Blyant.Validator.Css.Values;

/// <summary>
/// A component value: a single token, or a function / ( ) / [ ] block with its contents.
/// Whitespace and comments are left out: the grammar does not depend on them.
/// </summary>
public sealed class CssComponent(CssToken token, List<CssComponent>? children = null)
{
    public CssToken Token { get; } = token;

    /// <summary>The contents of a function or block; null for a plain token.</summary>
    public List<CssComponent>? Children { get; } = children;

    public CssTokenType Type => Token.Type;

    public static List<CssComponent> Build(IReadOnlyList<CssToken> tokens)
    {
        var index = 0;
        return BuildList(tokens, ref index, null);
    }

    private static List<CssComponent> BuildList(IReadOnlyList<CssToken> tokens, ref int index, CssTokenType? close)
    {
        var list = new List<CssComponent>();
        while (index < tokens.Count)
        {
            var token = tokens[index++];
            if (token.Type == close) return list;
            if (token.IsTrivia) continue;

            CssTokenType? childClose = token.Type switch
            {
                CssTokenType.Function or CssTokenType.LeftParen => CssTokenType.RightParen,
                CssTokenType.LeftSquare => CssTokenType.RightSquare,
                CssTokenType.LeftCurly => CssTokenType.RightCurly,
                _ => null,
            };
            list.Add(childClose is null
                ? new CssComponent(token)
                : new CssComponent(token, BuildList(tokens, ref index, childClose)));
        }
        return list;
    }

    /// <summary>All tokens of this component and its children, depth first.</summary>
    public IEnumerable<CssComponent> Descendants()
    {
        yield return this;
        if (Children is null) yield break;
        foreach (var child in Children)
            foreach (var d in child.Descendants())
                yield return d;
    }
}
