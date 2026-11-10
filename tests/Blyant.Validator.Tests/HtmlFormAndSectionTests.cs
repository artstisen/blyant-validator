using Blyant.Validator.Core;
using Blyant.Validator.Html;

namespace Blyant.Validator.Tests;

/// <summary>Forms, headings, sectioning, and the attributes of &lt;input&gt; per type.</summary>
public class HtmlFormAndSectionTests
{
    private const string Head = "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>T</title></head><body>";
    private const string Tail = "</body></html>";

    private static async Task<IReadOnlyList<Diagnostic>> InBody(string body) =>
        await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", Head + body + Tail, DocumentKind.Html));

    [Fact]
    public async Task RealisticFormsAndSections_HaveNoProblems()
    {
        const string body = """
            <header><hgroup><h1>Harbour Lights</h1><p>A novel</p></hgroup><nav><a href="/">Home</a></nav></header>
            <main>
              <article><header><h2>News</h2></header><p>Text</p><footer><address>Harbour office, <a href="tel:1">call</a></address></footer></article>
              <form action="/order" method="post">
                <label>Name <input name="name" required minlength="2" maxlength="80" pattern="[A-Za-z ]+" placeholder="Your name" autocomplete="name"></label>
                <label for="qty">Copies</label><input id="qty" type="number" min="1" max="10" step="1" value="1">
                <label for="when">Delivery</label><input id="when" type="date" min="2026-11-10" max="2026-12-31" value="2026-11-20">
                <input type="time" step="900" min="09:00" max="17:30:00">
                <input type="datetime-local" value="2026-11-10T14:30">
                <input type="month" value="2026-11"><input type="week" value="2026-W46">
                <input type="range" min="0" max="1" step="0.1" value=".5" list="marks"><datalist id="marks"><option value="0.5"></option></datalist>
                <input type="color" value="#336699"><input type="url" value="https://example.org/" placeholder="Web site">
                <input type="file" accept="image/*" multiple required>
                <label><input type="checkbox" name="gift" checked required> Gift wrap</label>
                <input type="password" autocomplete="current-password" minlength="8">
                <input type="search" dirname="q.dir" list="marks"><input type="hidden" name="t" value="x">
                <input type="image" src="buy.png" alt="Buy" width="80" height="20" formaction="/buy">
                <input type="submit" value="Order" formnovalidate><button type="reset">Clear</button>
                <meter value="0.6">60%</meter><progress value="3" max="10"></progress>
              </form>
              <h2><img src="logo.png" alt="Harbour Lights"></h2>
            </main>
            <footer><p>Footer</p></footer>
            """;
        Assert.Empty(await InBody(body));
    }

    [Theory]
    // Sectioning
    [InlineData("<header><footer>x</footer></header>", "html.content-model")]
    [InlineData("<footer><header>x</header></footer>", "html.content-model")]
    [InlineData("<address><h2>Contact</h2></address>", "html.content-model")]
    [InlineData("<article><main>x</main></article>", "html.content-model")]
    [InlineData("<header><main>x</main></header>", "html.content-model")]
    [InlineData("<dfn><dfn>x</dfn></dfn>", "html.content-model")]
    [InlineData("<progress><progress></progress></progress>", "html.content-model")]
    // Headings
    [InlineData("<hgroup><p>Only text</p></hgroup>", "html.content-model")]
    [InlineData("<hgroup><h1>A</h1><h2>B</h2></hgroup>", "html.content-model")]
    [InlineData("<hgroup><h1>A</h1><div>B</div></hgroup>", "html.content-model")]
    [InlineData("<h2>  </h2>", "html.empty-heading")]
    // Forms
    [InlineData("<label><input name=\"a\"><input name=\"b\"></label>", "html.content-model")]
    [InlineData("<label for=\"x\"><input id=\"y\"></label><input id=\"x\">", "html.content-model")]
    // Input attributes per type
    [InlineData("<input type=\"checkbox\" placeholder=\"x\">", "html.input-attribute")]
    [InlineData("<input type=\"text\" min=\"1\">", "html.input-attribute")]
    [InlineData("<input type=\"email\" checked>", "html.input-attribute")]
    [InlineData("<input type=\"text\" multiple>", "html.input-attribute")]
    [InlineData("<input type=\"file\" value=\"a.txt\">", "html.input-attribute")]
    [InlineData("<input type=\"number\" accept=\"image/*\">", "html.input-attribute")]
    [InlineData("<input type=\"hidden\" required>", "html.input-attribute")]
    [InlineData("<input type=\"password\" list=\"x\">", "html.input-attribute")]
    // Input values per type
    [InlineData("<input type=\"number\" value=\"three\">", "html.bad-value")]
    [InlineData("<input type=\"number\" min=\"1.\">", "html.bad-value")]
    [InlineData("<input type=\"date\" value=\"10/11/2026\">", "html.bad-value")]
    [InlineData("<input type=\"date\" max=\"2026-02-30\">", "html.bad-value")]
    [InlineData("<input type=\"time\" min=\"25:00\">", "html.bad-value")]
    [InlineData("<input type=\"month\" value=\"2026-13\">", "html.bad-value")]
    [InlineData("<input type=\"week\" value=\"2026-46\">", "html.bad-value")]
    [InlineData("<input type=\"datetime-local\" value=\"2026-11-10\">", "html.bad-value")]
    [InlineData("<input type=\"color\" value=\"red\">", "html.bad-value")]
    [InlineData("<input type=\"range\" step=\"0\">", "html.bad-value")]
    [InlineData("<input type=\"url\" value=\"example.org\">", "html.bad-value")]
    public async Task Problem_IsReported(string body, string code)
    {
        Assert.Contains(await InBody(body), d => d.Code == code);
    }

    [Fact]
    public async Task InputAttribute_TellsTheTypesItIsFor()
    {
        var d = Assert.Single(await InBody("<input type=\"text\" min=\"1\">"));
        Assert.Equal("Attribute “min” is only allowed when the input type is “date”, “datetime-local”, “month”, “number”, “range”, “time”, or “week”.", d.Message);
    }
}
