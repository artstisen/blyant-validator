using Blyant.Validator.Core;
using Blyant.Validator.Html;

namespace Blyant.Validator.Tests;

public class HtmlParseErrorTests
{
    private const string Head = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body>";
    private const string Tail = "</body></html>";

    private static async Task<IReadOnlyList<Diagnostic>> Validate(string html) =>
        await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html, DocumentKind.Html));

    private static async Task<IReadOnlyList<string>> CodesInBody(string body) =>
        [.. (await Validate(Head + body + Tail)).Select(d => d.Code)];

    [Theory]
    [InlineData("<p>a</p><p>b<ul><li>1<li>2</ul><table><tr><td>x<td>y</table><dl><dt>a<dd>b</dl>")]
    [InlineData("<img src=a.png alt=\"\"><br><input type=checkbox checked><svg><path d=\"M0 0\"/></svg>")]
    [InlineData("<p>&copy; &amp; &#169; &#xA9; a & b <a href=\"?a=1&b=2\">x</a></p>")]
    [InlineData("<!-- a -- comment --><select><option>A<option>B</select>")]
    [InlineData("<script>if (a < b && c) { x = '</div>'; }</script><textarea>a < b</textarea>")]
    public async Task ValidMarkup_HasNoErrors(string body)
    {
        Assert.Empty(await CodesInBody(body));
    }

    [Theory]
    [InlineData("<div><span>text</div>", "html.parse.unclosed-element")]
    [InlineData("<div>text</span></div>", "html.parse.stray-end-tag")]
    [InlineData("<div>text</p></div>", "html.parse.stray-end-tag")]
    [InlineData("<p>a</br></p>", "html.parse.br-end-tag")]
    [InlineData("<div class=a class=b></div>", "html.parse.duplicate-attribute")]
    [InlineData("<div class=\"a\"id=\"b\"></div>", "html.parse.no-space-between-attributes")]
    [InlineData("<a href=a\"b>x</a>", "html.parse.bad-unquoted-value")]
    [InlineData("<div/>", "html.parse.self-closing-non-void")]
    [InlineData("<div></div class=x>", "html.parse.end-tag-attributes")]
    [InlineData("<!--->", "html.parse.abrupt-comment")]
    [InlineData("<!-- a --!>", "html.parse.comment-bang")]
    [InlineData("<!-- <!-- -->", "html.parse.nested-comment")]
    [InlineData("<![CDATA[x]]>", "html.parse.cdata-in-html")]
    [InlineData("<?xml version=\"1.0\"?>", "html.parse.processing-instruction")]
    [InlineData("<table>text<tr><td>1</td></tr></table>", "html.parse.text-in-table")]
    [InlineData("<a href=x>1 <a href=y>2</a></a>", "html.parse.nested-element")]
    [InlineData("<body></body>", "html.parse.duplicate-structure")]
    [InlineData("<p>&#xZZ;</p>", "html.parse.bad-reference")]
    [InlineData("<p>&#169</p>", "html.parse.unterminated-reference")]
    [InlineData("<p>&copy 2026</p>", "html.parse.unterminated-reference")]
    [InlineData("<p>&#128;</p>", "html.parse.control-reference")]
    [InlineData("<p>&#0;</p>", "html.parse.bad-reference-codepoint")]
    [InlineData("<p>a </> b</p>", "html.parse.bogus-end-tag")]
    [InlineData("<p><b>bold<div>x</div></p>", "html.parse.unclosed-elements")]
    public async Task Error_IsReported(string body, string code)
    {
        Assert.Contains(code, await CodesInBody(body));
    }

    [Theory]
    [InlineData("<html><head><title>T</title></head></html>", "html.parse.missing-doctype")]
    [InlineData("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01//EN\"><html lang=\"en\"><head><title>T</title></head></html>", "html.parse.obsolete-doctype")]
    [InlineData(Head + "<!DOCTYPE html>" + Tail, "html.parse.stray-doctype")]
    [InlineData(Head + "<div><span>", "html.parse.eof-in-element")]
    [InlineData(Head + "<script>x", "html.parse.eof-in-element")]
    [InlineData(Head + "<!-- open", "html.parse.eof-in-comment")]
    [InlineData(Head + "<div class=\"x", "html.parse.eof-in-attribute")]
    public async Task DocumentLevelError_IsReported(string html, string code)
    {
        Assert.Contains(await Validate(html), d => d.Code == code);
    }

    [Theory]
    [InlineData("<!doctype html>")]
    [InlineData("<!DOCTYPE html SYSTEM \"about:legacy-compat\">")]
    public async Task ModernDoctypes_AreAccepted(string doctype)
    {
        var result = await Validate(doctype + "<html lang=\"en\"><head><title>T</title></head><body></body></html>");
        Assert.Empty(result);
    }

    [Fact]
    public async Task UnclosedElement_IsReportedAtItsStartTag()
    {
        var result = await Validate(Head + "\n<div>\n  <span>text\n</div>" + Tail);
        var d = Assert.Single(result, d => d.Code == "html.parse.unclosed-element");
        Assert.Equal(new SourcePosition(3, 3), d.Start);
    }
}
