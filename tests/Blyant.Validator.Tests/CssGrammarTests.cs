using Blyant.Validator.Css;
using Blyant.Validator.Core;

namespace Blyant.Validator.Tests;

/// <summary>
/// Real-world values of the properties whose grammars came last (grid, generated content, fonts, masks, border
/// images, SVG, timelines …): the valid ones must give no error, the wrong ones an invalid-value error.
/// </summary>
public class CssGrammarTests
{
    private static IReadOnlyList<Diagnostic> Errors(string property, string value) =>
        [.. CssValidator.Validate("t.css", $".a {{ {property}: {value} }}").Where(d => d.Severity == Severity.Error)];

    [Theory]
    // Grid
    [InlineData("grid-template-columns", "repeat(3, 1fr)")]
    [InlineData("grid-template-columns", "repeat(auto-fill, minmax(200px, 1fr))")]
    [InlineData("grid-template-columns", "repeat(auto-fit, minmax(min(100%, 18rem), 1fr))")]
    [InlineData("grid-template-columns", "[full-start] minmax(1rem, 1fr) [content-start] minmax(0, 60ch) [content-end] minmax(1rem, 1fr) [full-end]")]
    [InlineData("grid-template-columns", "200px 1fr auto")]
    [InlineData("grid-template-columns", "fit-content(40%) 1fr")]
    [InlineData("grid-template-columns", "subgrid")]
    [InlineData("grid-template-columns", "subgrid [a] [b]")]
    [InlineData("grid-template-columns", "none")]
    [InlineData("grid-template-rows", "auto 1fr auto")]
    [InlineData("grid-template-rows", "masonry")]
    [InlineData("grid-template-rows", "repeat(2, [row-start] 100px [row-end])")]
    [InlineData("grid-template-areas", "\"header header\" \"sidebar main\" \"footer footer\"")]
    [InlineData("grid-template", "\"a a a\" 40px \"b c c\" 40px / 1fr 1fr 1fr")]
    [InlineData("grid-template", "auto 1fr auto / 200px 1fr")]
    [InlineData("grid-template", "[header-left] \"head head\" 30px [header-right] / 120px 1fr")]
    [InlineData("grid", "auto-flow / 1fr 1fr")]
    [InlineData("grid", "auto 1fr / auto-flow dense 100px")]
    [InlineData("grid", "\"a b\" 1fr / auto 1fr")]
    [InlineData("grid-auto-rows", "minmax(100px, auto)")]
    [InlineData("grid-auto-columns", "1fr 2fr")]
    // Boxes, shapes and transforms
    [InlineData("transform-origin", "center")]
    [InlineData("transform-origin", "top left")]
    [InlineData("transform-origin", "50% 50% 0")]
    [InlineData("transform-origin", "left 10px")]
    [InlineData("transform-origin", "bottom right 2px")]
    [InlineData("clip-path", "circle(50%)")]
    [InlineData("clip-path", "polygon(50% 0, 100% 100%, 0 100%)")]
    [InlineData("clip-path", "inset(0 round 8px) border-box")]
    [InlineData("clip-path", "url(#clip)")]
    [InlineData("clip-path", "none")]
    [InlineData("shape-outside", "circle(50%) margin-box")]
    [InlineData("clip", "rect(0 0 0 0)")]
    [InlineData("clip", "rect(0, 0, 0, 0)")]
    // Generated content
    [InlineData("content", "\"\"")]
    [InlineData("content", "none")]
    [InlineData("content", "\"→ \" counter(item) \". \"")]
    [InlineData("content", "open-quote")]
    [InlineData("content", "url(icon.svg)")]
    [InlineData("content", "\"★\" / \"Favorite\"")]
    [InlineData("content", "counters(section, \".\") \" \"")]
    [InlineData("content", "attr(data-label)")]
    [InlineData("content", "\"\\201C\"")]
    [InlineData("quotes", "\"«\" \"»\" \"‹\" \"›\"")]
    [InlineData("quotes", "auto")]
    // Fonts
    [InlineData("font-variant", "small-caps")]
    [InlineData("font-variant", "tabular-nums slashed-zero")]
    [InlineData("font-variant", "common-ligatures small-caps")]
    [InlineData("font-variant-ligatures", "no-common-ligatures no-contextual")]
    [InlineData("font-variant-alternates", "styleset(ss01, ss02) historical-forms")]
    [InlineData("font-variant-east-asian", "jis04 full-width")]
    [InlineData("font-size-adjust", "0.5")]
    [InlineData("font-size-adjust", "ex-height from-font")]
    [InlineData("font-palette", "--my-palette")]
    // Border images and masks
    [InlineData("border-image", "url(border.png) 30 round")]
    [InlineData("border-image", "linear-gradient(red, blue) 1")]
    [InlineData("border-image", "url(b.png) 10% fill / 20px / 4px stretch")]
    [InlineData("border-image-slice", "30 fill")]
    [InlineData("border-image-width", "1 auto 2rem")]
    [InlineData("mask", "url(mask.svg) no-repeat center / contain")]
    [InlineData("mask-image", "linear-gradient(black, transparent)")]
    [InlineData("mask-image", "url(a.svg), none")]
    [InlineData("mask-size", "cover")]
    [InlineData("mask-position", "center")]
    [InlineData("mask-repeat", "no-repeat")]
    [InlineData("mask-mode", "luminance")]
    [InlineData("mask-composite", "intersect")]
    // Containers and view transitions
    [InlineData("container", "sidebar / inline-size")]
    [InlineData("container", "card")]
    [InlineData("container-name", "card main")]
    [InlineData("view-transition-name", "hero-image")]
    [InlineData("view-transition-name", "none")]
    [InlineData("contain-intrinsic-size", "auto 300px")]
    [InlineData("contain-intrinsic-size", "100px 200px")]
    // SVG
    [InlineData("fill", "currentColor")]
    [InlineData("fill", "none")]
    [InlineData("fill", "url(#gradient) red")]
    [InlineData("stroke", "#333")]
    [InlineData("stroke-width", "2")]
    [InlineData("stroke-width", "1.5px")]
    [InlineData("stroke-dasharray", "5 10")]
    [InlineData("stroke-dasharray", "4, 2, 1")]
    [InlineData("stroke-dashoffset", "-20")]
    [InlineData("paint-order", "stroke fill")]
    [InlineData("d", "path(\"M 0 0 L 10 10\")")]
    [InlineData("r", "10px")]
    [InlineData("vector-effect", "non-scaling-stroke")]
    // Scroll-driven animations and anchors
    [InlineData("animation-timeline", "scroll()")]
    [InlineData("animation-timeline", "view(block 10%)")]
    [InlineData("animation-timeline", "--my-timeline, auto")]
    [InlineData("scroll-timeline", "--page block")]
    [InlineData("view-timeline", "--card inline")]
    [InlineData("anchor-name", "--tooltip")]
    [InlineData("position-anchor", "--tooltip")]
    // Text
    [InlineData("text-emphasis", "filled circle red")]
    [InlineData("text-emphasis-position", "over right")]
    [InlineData("hanging-punctuation", "first last")]
    [InlineData("line-clamp", "3")]
    [InlineData("text-box", "trim-both cap alphabetic")]
    [InlineData("initial-letter", "3 2")]
    [InlineData("all", "unset")]
    public void ValidValues_HaveNoErrors(string property, string value)
    {
        Assert.Empty(Errors(property, value));
    }

    [Theory]
    [InlineData("grid-template-columns", "repeat(0, 1fr)")]
    [InlineData("grid-template-columns", "repeat(auto-fill, 1fr)")]
    [InlineData("grid-template-columns", "1fr red")]
    [InlineData("grid-template-columns", "[span] 1fr")]
    [InlineData("grid-template-areas", "header main")]
    [InlineData("grid-auto-rows", "-10px")]
    [InlineData("transform-origin", "left right")]
    [InlineData("clip-path", "circle(50%) red")]
    [InlineData("content", "12px")]
    [InlineData("quotes", "\"«\"")]
    [InlineData("font-variant", "small-caps small-caps")]
    [InlineData("font-variant", "bold")]
    [InlineData("border-image-slice", "-1")]
    [InlineData("mask-composite", "add-on")]
    [InlineData("container", "inline-size / sidebar")]
    [InlineData("view-transition-name", "a b")]
    [InlineData("stroke-width", "-2")]
    [InlineData("fill", "solid")]
    [InlineData("animation-timeline", "scroll")]
    [InlineData("line-clamp", "0")]
    public void WrongValues_AreErrors(string property, string value)
    {
        Assert.Contains(Errors(property, value), d => d.Code == "css.invalid-value");
    }
}
