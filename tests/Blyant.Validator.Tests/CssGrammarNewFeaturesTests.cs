using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Css.Values;

namespace Blyant.Validator.Tests;

/// <summary>The grammars of the newest features: anchor positioning, motion paths, mask borders and more.</summary>
public class CssGrammarNewFeaturesTests
{
    private static IReadOnlyList<Diagnostic> Errors(string property, string value) =>
        [.. CssValidator.Validate("t.css", $".a {{ {property}: {value} }}").Where(d => d.Severity == Severity.Error)];

    [Theory]
    [InlineData("position-area", "top center")]
    [InlineData("position-area", "span-left bottom")]
    [InlineData("position-area", "block-start span-inline-end")]
    [InlineData("position-area", "end")]
    [InlineData("position-area", "start span-end")]
    [InlineData("position-area", "none")]
    [InlineData("position-try-fallbacks", "flip-block")]
    [InlineData("position-try-fallbacks", "--above, flip-inline flip-block, bottom right")]
    [InlineData("position-try", "most-height --above, flip-block")]
    [InlineData("offset-path", "path(\"M 0 0 L 100 100\")")]
    [InlineData("offset-path", "ray(45deg closest-side)")]
    [InlineData("offset-path", "circle(50%) border-box")]
    [InlineData("offset-path", "url(#track)")]
    [InlineData("offset-rotate", "auto 90deg")]
    [InlineData("offset-rotate", "reverse")]
    [InlineData("offset-anchor", "left top")]
    [InlineData("offset-position", "normal")]
    [InlineData("offset", "path(\"M 0 0 H 100\") 50% auto")]
    [InlineData("offset", "left top ray(0deg) / center")]
    [InlineData("mask-border", "url(frame.svg) 30 round")]
    [InlineData("mask-border", "linear-gradient(black, transparent) 20 fill / 10px / 4px stretch alpha")]
    [InlineData("mask-border-slice", "10% fill")]
    [InlineData("mask-border-mode", "luminance")]
    [InlineData("animation-range", "entry 10% exit 90%")]
    [InlineData("animation-range", "cover")]
    [InlineData("animation-range", "normal, contain 0% contain 100%")]
    [InlineData("animation-range-start", "entry-crossing 25%")]
    [InlineData("corner-shape", "squircle")]
    [InlineData("corner-shape", "bevel round scoop notch")]
    [InlineData("corner-shape", "superellipse(1.5)")]
    [InlineData("reading-flow", "grid-rows")]
    [InlineData("reading-order", "-1")]
    [InlineData("scroll-marker-group", "after")]
    [InlineData("text-decoration-skip", "objects spaces edges")]
    [InlineData("image-resolution", "from-image 300dpi snap")]
    [InlineData("dynamic-range-limit", "constrained")]
    public void ValidValues_HaveNoErrors(string property, string value)
    {
        Assert.Empty(Errors(property, value));
    }

    [Theory]
    [InlineData("position-area", "top bottom")]
    [InlineData("position-area", "left inline-start")]
    [InlineData("position-try-fallbacks", "flip-block flip-block")]
    [InlineData("offset-rotate", "auto reverse")]
    [InlineData("mask-border-mode", "dark")]
    [InlineData("animation-range", "middle 10%")]
    [InlineData("corner-shape", "pointy")]
    [InlineData("reading-order", "1.5")]
    [InlineData("scroll-marker-group", "inside")]
    public void WrongValues_AreErrors(string property, string value)
    {
        Assert.Contains(Errors(property, value), d => d.Code == "css.invalid-value");
    }

    [Fact]
    public void OnlyTheNonStandardFontSmooth_IsLeftUnchecked()
    {
        var unchecked_ = CssData.Properties.Where(p => p.Value is null).Select(p => p.Key).ToList();
        Assert.Equal(["font-smooth"], unchecked_);
    }
}
