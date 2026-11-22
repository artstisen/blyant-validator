using Blyant.Validator.Core;
using Blyant.Validator.Html;

namespace Blyant.Validator.Tests;

/// <summary>The rules for obsolete features, attributes and content models.</summary>
public sealed class HtmlRuleTests
{
    private const string Head = "<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>T</title></head><body>";
    private const string Tail = "</body></html>";

    private static async Task<IReadOnlyList<Diagnostic>> Validate(string html) =>
        await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html, DocumentKind.Html));

    private static async Task<IReadOnlyList<Diagnostic>> InBody(string body) => await Validate(Head + body + Tail);

    /// <summary>A real-world page using many elements and attributes correctly: no problems at all</summary>
    [Fact]
    public async Task RealWorldPage_HasNoProblems()
    {
        const string page = """
            <!DOCTYPE html>
            <html lang="en" xmlns="http://www.w3.org/1999/xhtml">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta property="og:title" content="Harbour Lights">
              <meta http-equiv="X-UA-Compatible" content="IE=edge">
              <title>Harbour Lights</title>
              <link rel="stylesheet" href="css/site.css" media="screen">
              <link rel="icon" href="/favicon.svg" type="image/svg+xml" sizes="any">
              <link rel="preload" href="font.woff2" as="font" type="font/woff2" crossorigin>
              <script src="app.js" defer></script>
              <script type="module">import "./x.js";</script>
              <script type="application/ld+json">{"@type": "Book"}</script>
              <style>body { margin: 0 }</style>
            </head>
            <body class="home" data-page="index">
              <a class="skip" href="#main">Skip to content</a>
              <header>
                <nav aria-label="Main">
                  <ul>
                    <li><a href="/" aria-current="page">Home</a></li>
                    <li><a href="about.html" target="_blank" rel="noopener">About</a></li>
                  </ul>
                </nav>
              </header>
              <main id="main" tabindex="-1">
                <h1>Harbour <em>Lights</em></h1>
                <p>A novel. <span class="tag">New</span> <a href="buy.html"><img src="cover.jpg" alt="Cover" width="120" height="180" loading="lazy" decoding="async"></a></p>
                <picture>
                  <source srcset="hero.avif" type="image/avif">
                  <img src="hero.jpg" alt="" width="800" height="400">
                </picture>
                <figure><img src="map.png" alt="Map"><figcaption>The harbour</figcaption></figure>
                <dl><dt>Author</dt><dd>M. Holt</dd><div><dt>Year</dt><dd>2026</dd></div></dl>
                <ol start="3" type="a" reversed><li value="5">Five</li></ol>
                <table>
                  <caption>Lights</caption>
                  <thead><tr><th scope="col">Name</th><th scope="col">Signal</th></tr></thead>
                  <tbody><tr><td>North pier</td><td colspan="1">Red</td></tr></tbody>
                </table>
                <form action="/subscribe" method="post">
                  <label for="email">E-mail</label>
                  <input id="email" type="email" name="email" required autocomplete="email" placeholder="Your e-mail">
                  <label><input type="checkbox" name="news" checked> News</label>
                  <select name="topic"><optgroup label="Books"><option value="1" selected>One</option></optgroup></select>
                  <textarea name="msg" rows="3" cols="40" maxlength="500"></textarea>
                  <input type="hidden" name="token" value="abc">
                  <button type="submit" disabled>Send</button>
                </form>
                <details open><summary><h2>More</h2></summary><p>Hidden text</p></details>
                <video controls width="640" preload="metadata" playsinline><source src="clip.mp4" type="video/mp4"><track kind="captions" src="c.vtt" srclang="en" label="English" default></video>
                <svg viewBox="0 0 10 10" width="10" height="10"><circle cx="5" cy="5" r="4" fill="red"/></svg>
                <my-widget some-prop="1"></my-widget>
                <p hidden>Not shown</p>
                <div contenteditable spellcheck="false" dir="auto" draggable="true" translate="no" onclick="go()" style="color: red">Edit</div>
                <a href="javascript:void(0)">No-op</a>
              </main>
              <footer><address>Harbour office</address></footer>
            </body>
            </html>
            """;

        Assert.Empty(await Validate(page));
        // With the accessibility hints asked for: only the two that are true (the select and the textarea of the form
        // have no label), as info
        var hints = await new HtmlValidator(new HtmlCheckOptions(Accessibility: true))
            .ValidateAsync(new SourceDocument("t.html", page, DocumentKind.Html));
        Assert.Equal(["html.a11y.label", "html.a11y.label"], hints.Select(d => d.Code));
        Assert.All(hints, d => Assert.Equal(Severity.Info, d.Severity));
    }

    [Theory]
    // Obsolete
    [InlineData("<center>x</center>", "html.obsolete-element")]
    [InlineData("<font color=\"red\">x</font>", "html.obsolete-element")]
    [InlineData("<div align=\"center\">x</div>", "html.obsolete-attribute")]
    [InlineData("<table cellpadding=\"2\"><tr><td>x</td></tr></table>", "html.obsolete-attribute")]
    [InlineData("<a name=\"top\">x</a>", "html.obsolete-attribute")]
    [InlineData("<img src=\"a.png\" alt=\"\" border=\"1\">", "html.obsolete-attribute")]
    // Attributes
    [InlineData("<div foo=\"1\">x</div>", "html.unknown-attribute")]
    [InlineData("<blink2>x</blink2>", "html.unknown-element")]
    [InlineData("<input disabled=\"false\">", "html.bad-value")]
    [InlineData("<input type=\"txt\">", "html.bad-value")]
    [InlineData("<img src=\"a.png\" alt=\"\" width=\"100px\">", "html.bad-value")]
    [InlineData("<div id=\"a b\">x</div>", "html.bad-value")]
    [InlineData("<a href=\"my page.html\">x</a>", "html.bad-value")]
    [InlineData("<a href=\"docs\\page.html\">x</a>", "html.bad-value")]
    [InlineData("<form method=\"put\"></form>", "html.bad-value")]
    [InlineData("<ol type=\"x\"><li>1</li></ol>", "html.bad-value")]
    [InlineData("<table><tr><td colspan=\"0\">x</td></tr></table>", "html.bad-value")]
    [InlineData("<img alt=\"x\">", "html.missing-attribute")]
    [InlineData("<link href=\"a.css\">", "html.missing-attribute")]
    [InlineData("<label for=\"nothing\">x</label>", "html.label-for")]
    // Content models
    [InlineData("<ul><div>x</div></ul>", "html.content-model")]
    [InlineData("<div><li>x</li></div>", "html.content-model")]
    [InlineData("<span><div>x</div></span>", "html.content-model")]
    [InlineData("<span><a href=\"#\"><div>x</div></a></span>", "html.content-model")]
    [InlineData("<a href=\"#\"><button>x</button></a>", "html.content-model")]
    [InlineData("<button><a href=\"#\">x</a></button>", "html.content-model")]
    [InlineData("<table><tr><div>x</div></tr></table>", "html.content-model")]
    [InlineData("<figcaption>x</figcaption>", "html.content-model")]
    [InlineData("<main>a</main><main>b</main>", "html.content-model")]
    [InlineData("<ul>text<li>x</li></ul>", "html.text-not-allowed")]
    public async Task Problem_IsReported(string body, string code)
    {
        Assert.Contains(await InBody(body), d => d.Code == code);
    }

    [Theory]
    [InlineData("<img src=\"a.png\" alt=\"\" border=\"0\">")]
    [InlineData("<table border=\"1\"><tr><td>x</td></tr></table>")]
    [InlineData("<script type=\"text/javascript\">x()</script>")]
    [InlineData("<style type=\"text/css\">p{}</style>")]
    public async Task ObsoleteButConforming_IsAWarning(string body)
    {
        var problem = Assert.Single(await InBody(body));
        Assert.Equal(Severity.Warning, problem.Severity);
    }

    [Fact]
    public async Task ObsoleteElement_TellsWhatToUse()
    {
        var d = Assert.Single(await InBody("<center>x</center>"));
        Assert.Equal("The “center” element is obsolete. Use CSS instead.", d.Message);
    }

    [Theory]
    [InlineData("<ul>\n<li>Fast <a href=\"/f/\">fragt</a>/li>\n<li>b</li>\n</ul>", "/li>")]
    [InlineData("<ul><li>Normal/li>\n<li>b</li></ul>", "/li>")]
    [InlineData("<p>Text /p>", "/p>")]
    [InlineData("<p><strong>Bold/strong> text</p>", "/strong>")]
    [InlineData("<p><strong>Bold /strong> text</p>", "/strong>")]
    [InlineData("<p>A &amp;/p> B</p>", "/p>")]
    [InlineData("<ul><li>a /LI>\n<li>b</li></ul>", "/LI>")]
    public async Task TextThatLooksLikeAnEndTag_IsAWarning(string body, string text)
    {
        var d = Assert.Single(await InBody(body), d => d.Code == "html.text-looks-like-end-tag");
        Assert.Equal(Severity.Warning, d.Severity);
        Assert.Contains(text, d.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<p>and/or, km/h, 5/10, a/b> c, 1/p> and TCP/IP> are words, not end tags</p>")]
    [InlineData("<p>See https://example.com/p> or https://example.com/docs/li> and www.example.dk/strong></p>")]
    [InlineData("<p>Write /li&gt; and &lt;/li&gt; as text, or /notanelement></p>")]
    [InlineData("<pre>if (a /b> 0) x = 1 /p>;</pre><code>/li></code><p><kbd>/p></kbd></p>")]
    [InlineData("<svg viewBox=\"0 0 1 1\"><text>/g></text></svg>")]
    public async Task TextThatOnlyLooksLikeTagsInPart_IsNotTold(string body)
    {
        Assert.DoesNotContain(await InBody(body), d => d.Code == "html.text-looks-like-end-tag");
    }

    [Fact]
    public async Task ATextLikeAnEndTag_IsFoundInFragmentsToo()
    {
        var fragment = await new HtmlValidator(fragment: true).ValidateAsync(
            new SourceDocument("t.html", "<ul><li>a</a>/li><li>b</li></ul>", DocumentKind.Html));
        Assert.Contains(fragment, d => d.Code == "html.text-looks-like-end-tag");
    }

    [Fact]
    public async Task ALongValue_IsNotCutInsideAnEmoji()
    {
        var a = new string('a', 39);
        var d = Assert.Single(await InBody($"<img src=\"a.png\" alt=\"\" width=\"{a}😀\">"), d => d.Code == "html.bad-value");
        Assert.Contains($"“{a}…”", d.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(d.Message, char.IsSurrogate);
    }

    [Fact]
    public async Task AttributeProblem_PointsAtTheAttribute()
    {
        var d = Assert.Single(await Validate(Head + "\n<div  foo=\"1\">x</div>" + Tail));
        Assert.Equal(new SourcePosition(2, 7), d.Start);
    }
}
