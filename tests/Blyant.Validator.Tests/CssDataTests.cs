using Blyant.Validator.Css.Syntax;
using Blyant.Validator.Css.Values;

namespace Blyant.Validator.Tests;

/// <summary>Checks the embedded grammar files, so a mistake in them shows here and not as a false error.</summary>
public class CssDataTests
{
    [Fact]
    public void Data_IsLoaded()
    {
        Assert.True(CssData.Properties.Count > 400);
        Assert.True(CssData.Types.Count > 50);
    }

    [Fact]
    public void EveryReferencedTypeAndProperty_IsDefined()
    {
        var missing = new SortedSet<string>();
        foreach (var grammar in CssData.Properties.Values.OfType<GrammarNode>().Concat(CssData.Types.Values))
        {
            foreach (var node in Walk(grammar))
            {
                if (node is TypeNode t && !CssData.Types.ContainsKey(t.Name) &&
                    !CssData.BuiltInTypes.Contains(t.Name) && !CssData.FunctionTypes.ContainsKey(t.Name))
                    missing.Add($"<{t.Name}>");
                if (node is PropertyNode p && !CssData.Properties.ContainsKey(p.Property))
                    missing.Add($"<'{p.Property}'>");
            }
        }
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData("margin", "0 auto", true)]
    [InlineData("margin", "1px 2px 3px 4px 5px", false)]
    [InlineData("border", "1px solid red", true)]
    [InlineData("border", "solid solid", false)]
    [InlineData("background", "url(a.png) no-repeat center / cover, #fff", true)]
    [InlineData("background", "#fff, url(a.png)", false)]
    [InlineData("font", "italic bold 12px/30px Georgia, serif", true)]
    [InlineData("font", "12px", false)]
    [InlineData("transition", "opacity .3s, transform .3s ease-out 1s", true)]
    [InlineData("box-shadow", "inset 0 0 0 1px red, 0 2px 4px #0003", true)]
    [InlineData("display", "inline flex", true)]
    [InlineData("display", "flex grid", false)]
    [InlineData("grid-area", "1 / 2 / span 3 / 4", true)]
    [InlineData("width", "fit-content(20em)", true)]
    public void Grammar_MatchesExpectedValues(string property, string value, bool valid)
    {
        var grammar = CssData.Properties[property]!;
        var tokens = CssTokenizer.Tokenize(value, out _);
        tokens.RemoveAt(tokens.Count - 1);
        var result = GrammarMatcher.Match(grammar, CssComponent.Build(tokens));
        Assert.Equal(valid ? MatchResult.Valid : MatchResult.Invalid, result);
    }

    private static IEnumerable<GrammarNode> Walk(GrammarNode node)
    {
        yield return node;
        IEnumerable<GrammarNode> children = node switch
        {
            SequenceNode s => s.Items,
            AllOfNode a => a.Items,
            AnyOfNode a => a.Items,
            OneOfNode o => o.Items,
            RepeatNode r => [r.Item],
            FunctionNode { Arguments: { } args } => [args],
            _ => [],
        };
        foreach (var child in children)
            foreach (var n in Walk(child))
                yield return n;
    }
}
