using Blyant.Validator.Core;
using Blyant.Validator.Html;

namespace Blyant.Validator.Tests;

/// <summary>
/// The checks of HTML added in 0.6.2: errors the Nu Html Checker reports (the character encoding, a second title,
/// language tags, empty URLs …), typos and broken links (warnings), the warnings of the Nu Html Checker (roles,
/// sections without a heading), and the accessibility hints (info, only when asked for)
/// </summary>
public class HtmlMistakeTests
{
    private const string Head = "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>T</title></head><body>";
    private const string Tail = "</body></html>";

    private static async Task<IReadOnlyList<Diagnostic>> Check(string html, HtmlCheckOptions? options = null, bool bom = false) =>
        await new HtmlValidator(options ?? new HtmlCheckOptions())
            .ValidateAsync(new SourceDocument(Path.GetFullPath("t.html"), html, DocumentKind.Html) { HasUtf8Bom = bom });

    private static async Task<List<string>> Codes(string html, HtmlCheckOptions? options = null, bool bom = false) =>
        [.. (await Check(html, options, bom)).Select(d => d.Code)];

    private static Task<List<string>> InBody(string body, HtmlCheckOptions? options = null) => Codes(Head + body + Tail, options);

    // ---- Group 1: errors, as the Nu Html Checker reports them

    [Theory]
    [InlineData("<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body></body></html>", "html.charset-missing")]
    [InlineData("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"iso-8859-1\"><title>T</title></head><body></body></html>", "html.charset-mismatch")]
    [InlineData("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta charset=\"utf-8\"><title>T</title></head><body></body></html>", "html.charset-twice")]
    [InlineData("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>A</title><title>B</title></head><body></body></html>", "html.title-twice")]
    public async Task ThePage_IsCheckedAsTheNuHtmlCheckerChecksIt(string html, string code)
    {
        Assert.Contains(code, await Codes(html));
        // (in a fragment, the page around it has these)
        Assert.DoesNotContain(code, await Codes(html, new HtmlCheckOptions(Fragment: true)));
    }

    [Fact]
    public async Task TheCharacterEncoding_IsDeclaredInTheFirst1024Bytes_OrByABom()
    {
        var late = "<!DOCTYPE html><html lang=\"en\"><head><!--" + new string('x', 1100) + "--><meta charset=\"utf-8\"><title>T</title></head><body></body></html>";
        Assert.Contains("html.charset-late", await Codes(late));
        var none = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body></body></html>";
        Assert.Empty(await Codes(none, bom: true));
        var httpEquiv = "<!DOCTYPE html><html lang=\"en\"><head><meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\"><title>T</title></head><body></body></html>";
        Assert.Empty(await Codes(httpEquiv));
    }

    [Fact]
    public async Task ATitleInSvg_IsNotASecondTitle()
    {
        Assert.Empty(await InBody("<svg viewBox=\"0 0 1 1\"><title>Icon</title></svg>"));
    }

    [Theory]
    [InlineData("english", "use a code")]
    [InlineData("en_US", "write “en-US”")]
    [InlineData("en US", "use a code")]
    public async Task ALanguage_IsALanguageTag(string lang, string advice)
    {
        var d = Assert.Single(await Check(Head + $"<p lang=\"{lang}\">x</p>" + Tail));
        Assert.Equal(("html.bad-lang", Severity.Error), (d.Code, d.Severity));
        Assert.Contains(advice, d.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("da")]
    [InlineData("en-US")]
    [InlineData("zh-Hant-TW")]
    [InlineData("sr-Latn-RS")]
    [InlineData("de-CH-1996")]
    [InlineData("es-419")]
    [InlineData("x-klingon")]
    [InlineData("")]
    public async Task LanguageTags_AreTaken(string lang)
    {
        Assert.Empty(await InBody($"<p lang=\"{lang}\" xml:lang=\"{lang}\">x</p>"));
    }

    [Theory]
    [InlineData("<img src=\"\" alt=\"x\">", "html.empty-url")]
    [InlineData("<script src=\"\"></script>", "html.empty-url")]
    [InlineData("<form action=\"\"></form>", "html.empty-url")]
    [InlineData("<script src=\"a.js\">alert(1)</script>", "html.script-src-content")]
    [InlineData("<p class=>Text</p>", "html.parse.missing-attribute-value")]
    public async Task Error_IsReported(string body, string code)
    {
        Assert.Contains(code, await InBody(body));
    }

    [Theory]
    [InlineData("<a href=\"\">This page</a>")]
    [InlineData("<script src=\"a.js\">/* a comment */ // and another\n</script>")]
    [InlineData("<script src=\"a.js\" type=\"text/template\"><p>{{x}}</p></script>")]
    [InlineData("<p class=\"\">Empty, and quoted</p><input value=\"\">")]
    public async Task ValidMarkup_IsNotReported(string body)
    {
        Assert.Empty(await InBody(body));
    }

    [Fact]
    public async Task ContentAfterTheBody_IsAnError_Once_ButNotAComment()
    {
        var after = await Codes(Head + "<p>x</p></body></html>\n<script>x()</script>\n<p>more</p>");
        Assert.Single(after, c => c == "html.parse.content-after-body");
        Assert.Empty(await Codes(Head + "<p>x</p></body></html>\n<!-- built 2026-11-19 -->\n"));
    }

    // ---- Group 2: typos and broken links (warnings)

    [Theory]
    [InlineData("<img src=a.png alt=x/>", "html.unquoted-slash")]
    [InlineData("<p>Text</p>>", "html.stray-bracket")]
    [InlineData("<ul><li>One</li>\nli>Two</li></ul>", "html.text-looks-like-start-tag")]
    [InlineData("<p><a href=\"#intro\">Intro</a></p>", "html.broken-anchor")]
    public async Task Typo_IsAWarning(string body, string code)
    {
        var d = Assert.Single(await Check(Head + body + Tail), d => d.Code == code);
        Assert.Equal(Severity.Warning, d.Severity);
    }

    [Theory]
    [InlineData("<img src=\"a.png\" alt=\"x\"/><br/><a href=/about/>About</a>")]
    [InlineData("<p>a > b, and a->b, and 5 >= 3</p><pre>x = a >> b;</pre><p><code>>></code></p>")]
    [InlineData("<p>The selector ul>li picks the items, and so does ol> li.</p>")]
    [InlineData("<h2 id=\"intro\">Intro</h2><p><a href=\"#intro\">Up</a> <a href=\"#\">Top</a> <a href=\"#top\">Top</a> <a name=\"old\"></a><a href=\"#old\">Old</a></p>")]
    [InlineData("<h2 id=\"caf%C3%A9\">x</h2><p><a href=\"#caf%C3%A9\">x</a></p>")]
    public async Task WhatOnlyLooksLikeATypo_IsNotTold(string body)
    {
        string[] typos = ["html.unquoted-slash", "html.stray-bracket", "html.text-looks-like-start-tag", "html.broken-anchor"];
        Assert.DoesNotContain(await InBody(body), typos.Contains);
    }

    [Fact]
    public async Task AnchorsAreNotChecked_InAFragment_OrWithABase()
    {
        Assert.Empty(await Codes("<p><a href=\"#intro\">Intro</a></p>", new HtmlCheckOptions(Fragment: true)));
        Assert.DoesNotContain("html.broken-anchor",
            await Codes("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>T</title><base href=\"/docs/\"></head><body><a href=\"#x\">x</a></body></html>"));
    }

    [Fact]
    public async Task RelativeLinks_AreLookedUp_ThroughTheGivenFunction()
    {
        var asked = new List<string>();
        var options = new HtmlCheckOptions(Exists: path =>
        {
            asked.Add(path);
            return path.EndsWith("there.png", StringComparison.Ordinal);
        });
        var body = "<img src=\"img/there.png\" alt=\"a\"><img src=\"img/gone.png\" alt=\"b\"><a href=\"page.html?x=1#top\">p</a>" +
                   "<a href=\"https://example.com/x.html\">w</a><a href=\"/root.html\">r</a><a href=\"mailto:a\">m</a>" +
                   "<a href=\"{{ url }}\">t</a><a href=\"dir%20name/a.html\">d</a>";
        var found = (await Check(Head + body + Tail, options)).Where(d => d.Code == "html.missing-file").ToList();
        Assert.Equal(3, found.Count); // gone.png, page.html and dir name/a.html
        Assert.Contains(Path.GetFullPath(Path.Combine("img", "there.png")), asked);
        Assert.Contains(Path.GetFullPath(Path.Combine("dir name", "a.html")), asked);
        Assert.Equal(4, asked.Count);
        Assert.DoesNotContain("html.missing-file", await Codes(body, options with { Fragment = true }));
    }

    // ---- Group 3: warnings of the Nu Html Checker

    [Theory]
    [InlineData("<button role=\"button\">OK</button>", 1)]
    [InlineData("<nav role=\"navigation\"><a href=\"/\">Home</a></nav>", 1)]
    [InlineData("<input type=\"checkbox\" role=\"checkbox\" aria-label=\"x\">", 1)]
    [InlineData("<ul role=\"list\"><li role=\"listitem\">a</li></ul>", 0)]
    [InlineData("<a role=\"link\" tabindex=\"0\">no href</a><div role=\"button\">x</div>", 0)]
    public async Task ARoleTheElementHasAnyway_IsAWarning(string body, int count)
    {
        Assert.Equal(count, (await InBody(body)).Count(c => c == "html.redundant-role"));
    }

    [Theory]
    [InlineData("<section><p>Text</p></section>", 1)]
    [InlineData("<article><p>Text</p></article>", 1)]
    [InlineData("<section><div><h2>Heading</h2></div><p>Text</p></section>", 0)]
    [InlineData("<section aria-label=\"Contact\"><p>Text</p></section>", 0)]
    [InlineData("<section><section><h2>Inner</h2></section></section>", 1)]
    public async Task ASectionWithoutAHeading_IsAWarning(string body, int count)
    {
        Assert.Equal(count, (await InBody(body)).Count(c => c == "html.section-no-heading"));
    }

    // ---- Group 4: accessibility hints (info, when asked for)

    private static readonly HtmlCheckOptions Hints = new(Accessibility: true);

    [Theory]
    [InlineData("<p><a href=\"/next\"></a></p>", "html.a11y.link-name")]
    [InlineData("<button></button>", "html.a11y.button-name")]
    [InlineData("<input type=\"text\" name=\"q\" placeholder=\"Search\">", "html.a11y.label")]
    [InlineData("<h1>Title</h1><h3>Skipped</h3>", "html.a11y.heading-order")]
    [InlineData("<iframe src=\"https://example.com/\"></iframe>", "html.a11y.frame-title")]
    public async Task AHint_IsInfo_OnlyWhenAskedFor(string body, string code)
    {
        var d = Assert.Single(await Check(Head + body + Tail, Hints), d => d.Code == code);
        Assert.Equal(Severity.Info, d.Severity);
        Assert.DoesNotContain(code, await InBody(body));
    }

    [Theory]
    [InlineData("<p><a href=\"/\">Home</a> <a href=\"/\"><img src=\"https://example.com/l.png\" alt=\"Home\"></a> <a href=\"/\" aria-label=\"Home\"><svg viewBox=\"0 0 1 1\"></svg></a></p>")]
    [InlineData("<p><a href=\"/\"><svg viewBox=\"0 0 1 1\"><title>Home</title></svg></a> <button title=\"Close\">×</button> <button><span>OK</span></button></p>")]
    [InlineData("<label>Name <input type=\"text\"></label><label for=\"e\">Mail</label><input id=\"e\" type=\"email\"><input type=\"hidden\" name=\"t\"><input type=\"submit\"><select aria-label=\"Size\"><option>S</option></select>")]
    [InlineData("<h2>Starts at two</h2><h3>Three</h3><h2>Two</h2><h3>Three</h3><h1>One</h1>")]
    [InlineData("<iframe src=\"https://example.com/\" title=\"Map\"></iframe>")]
    public async Task WhatIsAccessible_GetsNoHint(string body)
    {
        var html = Head.Replace("<title>", "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>", StringComparison.Ordinal) + body + Tail;
        Assert.Empty(await Codes(html, Hints));
    }

    [Fact]
    public async Task APageWithoutAViewport_GetsAHint_ButNotAFragment()
    {
        Assert.Contains("html.a11y.viewport", await Codes(Head + Tail, Hints));
        Assert.Empty(await Codes("<p>Text</p>", Hints with { Fragment = true }));
    }
}
