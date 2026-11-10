using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Html;
using Blyant.Validator.Markdown;
using Blyant.Validator.Xml;

namespace Blyant.Validator.Tests;

/// <summary>
/// Valid files that were reported with problems (found in the code review): they must stay without problems. A false
/// error is worse than a missed one.
/// </summary>
public class FalsePositiveTests
{
    private static async Task<IReadOnlyList<Diagnostic>> Html(string html) =>
        await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html, DocumentKind.Html));

    private const string Page = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body>";

    // ---- HTML

    [Theory]
    // The start and end tags of head and body may be left out
    [InlineData("<!DOCTYPE html><html lang=en><meta charset=utf-8><title>T</title><p>Hello")]
    [InlineData("<!DOCTYPE html><html lang=en><head><title>T</title><p>Hello</p>")]
    [InlineData("<!DOCTYPE html><html lang=en><title>T</title>Hello <b>world</b>")]
    [InlineData("<!DOCTYPE html><html lang=en><meta charset=utf-8><title>T</title><body><p>x</p></body></html>")]
    [InlineData("<!DOCTYPE html>\n<html lang=en>\n<head>\n<meta charset=utf-8>\n<title>T</title>\n<link rel=stylesheet href=a.css>\n<h1>Hi</h1>\n")]
    // Table parts whose tags may be left out
    [InlineData(Page + "<table><col span=2><tr><td>1</td><td>2</td></tr></table>")]
    [InlineData(Page + "<table><colgroup><col><col><thead><tr><th>a<th>b<tbody><tr><td>1<td>2</table>")]
    [InlineData(Page + "<table><caption>Prices<tr><td>1</table>")]
    [InlineData(Page + "<table><caption>Prices<colgroup><col></colgroup><tbody><tr><td>1</table>")]
    // An a in SVG in an a; a comment that ends with "<!-->"; li, dt, dd and summary close a p
    [InlineData(Page + "<a href=\"/\"><svg viewBox=\"0 0 9 9\"><a href=\"#x\"><text>t</text></a></svg></a>")]
    [InlineData(Page + "<!-- old <!--><p>x</p>")]
    [InlineData(Page + "<ul><li><p>one<li><p>two</ul><dl><dt><p>a<dd><p>b</dl><details><summary><p>s</summary>x</details>")]
    // IDs in a template are of its own document
    [InlineData(Page + "<template><p id=\"x\">copy</p></template><p id=\"x\">page</p>")]
    // Character references in a style attribute; CSS in a CDATA section of SVG
    [InlineData(Page + "<p style=\"font-family: &quot;Open Sans&quot;, sans-serif; content: &#34;x&#x22;\">x</p>")]
    [InlineData(Page + "<svg viewBox=\"0 0 9 9\"><style><![CDATA[ .a { fill: red } ]]></style></svg>")]
    // Newer HTML: dirname on more input types, CSS colors with alpha, customizable select, xmlns
    [InlineData(Page + "<input type=\"email\" name=\"e\" dirname=\"e.dir\"><input type=\"submit\" dirname=\"s.dir\">")]
    [InlineData(Page + "<input type=\"color\" alpha value=\"rgb(0 0 0 / 50%)\">")]
    [InlineData(Page + "<select><button><selectedcontent></selectedcontent></button><div><option>a</option></div><hr><option>b</option></select>")]
    [InlineData(Page + "<div xmlns=\"http://www.w3.org/1999/xhtml\">x</div>")]
    public async Task ValidHtml_HasNoProblems(string html)
    {
        Assert.Empty(await Html(html));
    }

    [Fact]
    public async Task MetadataAfterTheBody_IsStillAnError()
    {
        var problems = await Html("<!DOCTYPE html><html lang=en><head><title>T</title></head><body><p>x</p></body><meta charset=utf-8></html>");
        Assert.Contains(problems, d => d.Code == "html.content-model");
    }

    [Fact]
    public async Task ATableBackground_IsOnlyObsolete()
    {
        var problems = await Html(Page + "<table background=\"a.png\"><tr><td>1</table>");
        Assert.Equal("html.obsolete-attribute", Assert.Single(problems).Code);
    }

    [Fact]
    public async Task AProblemInAStyleAttributeWithReferences_IsAtItsPlace()
    {
        var problems = await Html(Page + "\n<p style=\"font-family: &quot;Open Sans&quot;, sans-serif; colr: red\">x</p>");
        var d = Assert.Single(problems);
        Assert.Equal("css.unknown-property", d.Code);
        Assert.Equal(new SourcePosition(2, 59), d.Start);
    }

    // ---- XML

    [Fact]
    public void EntitiesDeclaredInTheFile_AreKnown()
    {
        const string svg = """
            <?xml version="1.0"?>
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd" [
            <!ENTITY ns_svg "http://www.w3.org/2000/svg">
            <!ENTITY ns_xlink "http://www.w3.org/1999/xlink">
            ]>
            <svg xmlns="&ns_svg;" xmlns:xlink="&ns_xlink;" width="1" height="1"/>
            """;
        Assert.Empty(XmlValidator.Validate("ai.svg", svg));
    }

    [Fact]
    public void AnEntityOfAnExternalDtd_IsAWarning()
    {
        const string xhtml = """
            <?xml version="1.0"?>
            <!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Strict//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-strict.dtd">
            <html xmlns="http://www.w3.org/1999/xhtml"><body><p>a&nbsp;b</p></body></html>
            """;
        var d = Assert.Single(XmlValidator.Validate("page.xml", xhtml));
        Assert.Equal(("xml.undeclared-entity", Severity.Warning), (d.Code, d.Severity));
    }

    [Fact]
    public void AnUndeclaredEntity_WithoutADtd_IsStillAnError()
    {
        Assert.Equal("xml.not-well-formed", Assert.Single(XmlValidator.Validate("a.xml", "<a>&nbsp;</a>")).Code);
    }

    [Fact]
    public void EntitiesThatExpandWithoutEnd_AreStopped()
    {
        var entities = string.Concat(Enumerable.Range(1, 8).Select(n =>
            $"<!ENTITY e{n} \"{string.Concat(Enumerable.Repeat($"&e{n - 1};", 10))}\">"));
        var xml = $"<!DOCTYPE r [<!ENTITY e0 \"aaaaaaaaaa\">{entities}]><r>&e8;</r>";
        var d = Assert.Single(XmlValidator.Validate("bomb.xml", xml));
        Assert.Equal("xml.not-well-formed", d.Code);
        Assert.Contains("10,000,000", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Xhtml_IsXml()
    {
        Assert.Equal(DocumentKind.Xml, SourceDocument.DetectKind("chapter1.xhtml"));
    }

    [Fact]
    public void AnAtomLeapSecond_IsValid()
    {
        const string atom = """
            <feed xmlns="http://www.w3.org/2005/Atom">
              <id>urn:uuid:60a76c80-d399-11d9-b93C-0003939e0af6</id>
              <title>T</title>
              <updated>2016-12-31T23:59:60Z</updated>
              <author><name>A</name></author>
              <link href="https://a.example/"/>
            </feed>
            """;
        Assert.Empty(XmlValidator.Validate("feed.atom", atom));
    }

    // ---- Markdown and llms.txt

    [Theory]
    [InlineData("# Title\n\n<!--\n# Old title\n[x](missing.md)\n-->\n\nText.")]
    [InlineData("# Title\n\n<details>\n<summary>More</summary>\n# Not a heading\n</details>\n")]
    [InlineData("# Title\n\n- item\n\n    ```\n    # not a heading\n    ```\n")]
    [InlineData("# Title\n\n> ```\n> # quoted code\n> ```\n")]
    [InlineData("# Title\n\nUse `x` and arr[0][1] here, or matrix[i][j].")]
    [InlineData("# Title\n\nA long\nsetext heading\n--------------\n\n[jump](#a-long-setext-heading)\n")]
    public void ValidMarkdown_HasNoWarnings(string markdown)
    {
        Assert.Empty(MarkdownValidator.Validate("README.md", markdown, _ => false));
    }

    [Fact]
    public void LlmsTxt_WithParenthesesBracketsAndNestedItems_IsValid()
    {
        const string llms = """
            # Name

            > Summary

            ## Docs

            - [Foo](https://en.wikipedia.org/wiki/Foo_(bar)): notes
            - [Guide [beta]](https://a.example/guide)
                - [Sub](https://a.example/sub)
            """;
        Assert.Empty(MarkdownValidator.Validate("llms.txt", llms, _ => false));
    }

    // ---- CSS

    [Theory]
    [InlineData("@scope (.card) { > img { width: 100% } + .note { color: red } color: blue }")]
    [InlineData("::-webkit-scrollbar-thumb:horizontal { background: gray } ::-webkit-scrollbar-button:vertical:decrement { display: none }")]
    [InlineData(".a { font: -apple-system-body }")]
    [InlineData(".a { color-adjust: exact; inset-area: top }")]
    [InlineData("@function --double(--x <length>) returns <length> { result: calc(var(--x) * 2) } .a { width: --double(4px) }")]
    public void ValidCss_HasNoErrorsOrWarnings(string css)
    {
        Assert.DoesNotContain(CssValidator.Validate("a.css", css), d => d.Severity != Severity.Info);
    }
}
