using Blyant.Validator.Core;
using Blyant.Validator.Css;

namespace Blyant.Validator.Tests;

public class CssValidatorTests
{
    private static IReadOnlyList<Diagnostic> Validate(string css) => CssValidator.Validate("test.css", css);

    private static IReadOnlyList<Diagnostic> Errors(string css) =>
        [.. Validate(css).Where(d => d.Severity == Severity.Error)];

    [Fact]
    public void RealWorldStyleSheet_HasNoErrors()
    {
        const string css = """
            @charset "utf-8";
            @import url("reset.css") layer(base);
            :root { --brand: #3b82f6; color-scheme: light dark; }
            *, *::before, *::after { box-sizing: border-box; margin: 0; }
            html { font: 400 1rem/1.5 "Inter", system-ui, -apple-system, sans-serif; }
            body { background: #fff url("bg.png") no-repeat center / cover fixed; color: rgb(20 20 20 / .9); min-height: 100dvh; }
            a:hover, a:focus-visible { color: var(--brand); text-decoration: underline 2px; }
            .card { display: flex; flex: 1 1 0%; padding: 1rem 1.5rem; border: 1px solid hsl(220 13% 91%); border-radius: 8px / 4px;
              box-shadow: 0 1px 2px rgba(0,0,0,.05), inset 0 0 0 1px #0001; transition: transform .2s ease-in-out, opacity 150ms; }
            .card > h2 + p ~ span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
            .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); grid-column: 1 / -1; place-items: center start; }
            .btn:not(:disabled):active { filter: brightness(.95) drop-shadow(0 0 2px red); transform: translateY(-1px) scale(1.02); }
            img { max-width: 100%; height: auto; aspect-ratio: 16 / 9; object-fit: cover; }
            .nested { color: red; &:hover { color: blue; } > .child { margin-inline: auto; } }
            @media (min-width: 640px) { .card { animation: fade-in .3s cubic-bezier(.2,.8,.2,1) both; } }
            @keyframes fade-in { from { opacity: 0; } 50% { opacity: .5 } to { opacity: 1; } }
            @font-face { font-family: "Inter"; src: url(inter.woff2) format("woff2"); font-display: swap; }
            @layer base, components;
            .w { margin: 0 auto !important; padding: calc(1rem + 2px) max(1rem, 5%); width: min(100% - 2rem, 60ch); }
            .t { background-position: right 10px top 5px; font-style: oblique 10deg; outline: 2px solid AccentColor; }
            """;

        Assert.Empty(Errors(css));
    }

    [Theory]
    [InlineData(".a { colr: red }", "css.unknown-property")]
    [InlineData(".a { color: reddd }", "css.invalid-value")]
    [InlineData(".a { margin: 10 px }", "css.invalid-value")]
    [InlineData(".a { padding: -5px }", "css.invalid-value")]
    [InlineData(".a { width: 10pz }", "css.unknown-unit")]
    [InlineData(".a { z-index: 1.5 }", "css.invalid-value")]
    [InlineData(".a { font-weight: 1200 }", "css.invalid-value")]
    [InlineData(".a { opacity: ; }", "css.empty-value")]
    [InlineData(".a { color red }", "css.parse.missing-colon")]
    [InlineData(".a:hovr { color: red }", "css.selector.unknown-pseudo-class")]
    [InlineData(".a::befor { color: red }", "css.selector.unknown-pseudo-element")]
    [InlineData("#1st { color: red }", "css.selector.bad-id")]
    [InlineData(".a > { color: red }", "css.selector.trailing-combinator")]
    [InlineData(".a, { color: red }", "css.selector.empty")]
    [InlineData(".a { color: red; } @import 'b.css';", "css.import-position")]
    [InlineData(".a { color: 'red }", "css.parse.unclosed-string")]
    [InlineData(".a { color: red", "css.parse.unclosed-block")]
    [InlineData("@font-face { colour: red }", "css.unknown-descriptor")]
    [InlineData("@keyframes x { middle { opacity: 0 } }", "css.keyframe-selector")]
    public void Error_IsReported(string css, string code)
    {
        Assert.Contains(Errors(css), d => d.Code == code);
    }

    [Fact]
    public void UnknownProperty_SuggestsCloseName()
    {
        var d = Assert.Single(Errors(".a { backround-color: red }"));
        Assert.Contains("background-color", d.Message);
    }

    [Fact]
    public void Position_PointsAtTheProblem()
    {
        var d = Assert.Single(Errors(".a {\n  color: red;\n  margin: x;\n}"));
        Assert.Equal(new SourcePosition(3, 11), d.Start);
    }

    [Theory]
    [InlineData(".a { color: var(--c) }")]
    [InlineData(".a { --anything: {}; --x: 1 2 3 !; }")]
    [InlineData(".a { color: inherit }")]
    [InlineData(".a { margin: revert-layer }")]
    [InlineData(".a { grid-template-columns: [full-start] minmax(1rem, 1fr) [content-start] }")]
    [InlineData(".a { width: calc(100% - var(--x)) }")]
    public void ValuesThatCannotBeChecked_AreAccepted(string css)
    {
        Assert.Empty(Errors(css));
    }

    [Fact]
    public void VendorExtensions_AreInfo()
    {
        var result = Validate(".a { -webkit-line-clamp: 3; display: -webkit-box } ::-moz-selection { color: red }");
        Assert.NotEmpty(result);
        Assert.All(result, d => Assert.Equal(Severity.Info, d.Severity));
    }

    [Fact]
    public void UnknownAtRule_IsWarning()
    {
        var d = Assert.Single(Validate("@medai screen { .a { color: red } }"));
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Equal("css.unknown-at-rule", d.Code);
    }

    [Fact]
    public void NestedRules_MayStartWithCombinator()
    {
        Assert.Empty(Errors(".a { > .b { color: red } + .c { color: blue } }"));
        Assert.Contains(Errors("> .b { color: red }"), d => d.Code == "css.selector.leading-combinator");
    }
}
