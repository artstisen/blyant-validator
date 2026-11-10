using System.Text;
using Blyant.Validator.Cli;
using Blyant.Validator.Cli.Tui;
using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Css.Syntax;
using Blyant.Validator.Html;
using Blyant.Validator.Vnu;
using Blyant.Validator.Xml;

namespace Blyant.Validator.Tests;

/// <summary>
/// Files that must not crash the program or be changed by formatting beyond whitespace: digits of other scripts,
/// deep nesting, files that are not UTF-8, old Mac line endings, and the cases found in the code review.
/// </summary>
public sealed class SafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blyant-validator-safety-" + Guid.NewGuid().ToString("N"));

    public SafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static readonly FormatOptions Options = new();

    private static string Format(IFormatter formatter, string path, string text, DocumentKind kind) =>
        formatter.Format(new SourceDocument(path, text, kind), Options);

    // ---- Digits of other scripts (\d in .NET matches them, int.Parse does not take them)

    [Fact]
    public void ArabicIndicDigits_InASitemapDate_AreAnError_NotACrash()
    {
        const string xml = "<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\"><url><loc>https://a.example/</loc>" +
                           "<lastmod>٢٠٢٦-11-10</lastmod><priority>٠.5</priority></url></urlset>";
        var codes = XmlValidator.Validate("sitemap.xml", xml).Select(d => d.Code).ToList();
        Assert.Contains("sitemap.bad-lastmod", codes);
        Assert.Contains("sitemap.bad-priority", codes);
    }

    [Fact]
    public void ArabicIndicDigits_InAFeedDate_AreAnError_NotACrash()
    {
        const string rss = "<rss version=\"2.0\"><channel><title>T</title><link>https://a.example/</link><description>D</description>" +
                           "<pubDate>Tue, ١٠ Nov 2026 14:30:00 GMT</pubDate></channel></rss>";
        Assert.Contains(XmlValidator.Validate("feed.xml", rss), d => d.Code == "rss.bad-date");
    }

    [Theory]
    [InlineData("month", "2026-١١")]
    [InlineData("week", "2026-W٤٦")]
    [InlineData("time", "١٤:30")]
    public async Task ArabicIndicDigits_InAnInputValue_AreAnError_NotACrash(string type, string value)
    {
        var html = $"<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body><input type=\"{type}\" value=\"{value}\">";
        var problems = await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html, DocumentKind.Html));
        Assert.Contains(problems, d => d.Code == "html.bad-value");
    }

    [Fact]
    public async Task AMonth_WithAFiveDigitYear_IsValid()
    {
        var html = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body><input type=\"month\" value=\"10000-11\">";
        Assert.Empty(await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html, DocumentKind.Html)));
    }

    // ---- Deep nesting (the checks and formatters recurse: the stack must never overflow)

    [Fact]
    public async Task DeepHtml_IsReported_AndNotFormatted()
    {
        var html = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body>" + string.Concat(Enumerable.Repeat("<b>", 5000));
        var problems = await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html, DocumentKind.Html));
        Assert.Contains(problems, d => d.Code == "html.too-deep");
        Assert.Throws<Core.FormatException>(() => Format(new HtmlFormatter(), "t.html", html, DocumentKind.Html));
    }

    [Fact]
    public void DeepCss_IsReported_AndNotFormatted()
    {
        var css = string.Concat(Enumerable.Repeat("a { ", 3000)) + "color: red" + new string('}', 3000);
        Assert.Contains(CssValidator.Validate("t.css", css), d => d.Code == "css.parse.too-deep");
        Assert.Throws<Core.FormatException>(() => Format(new CssFormatter(), "t.css", css, DocumentKind.Css));

        var value = ".a { width: " + string.Concat(Enumerable.Repeat("calc(", 3000)) + "1px" + new string(')', 3000) + " }";
        Assert.Contains(CssValidator.Validate("t.css", value), d => d.Code == "css.parse.too-deep");
    }

    [Fact]
    public void DeepXml_IsChecked_ButNotFormatted()
    {
        var xml = string.Concat(Enumerable.Repeat("<a>", 3000)) + string.Concat(Enumerable.Repeat("</a>", 3000));
        Assert.Empty(XmlValidator.Validate("t.xml", xml));
        Assert.Throws<Core.FormatException>(() => Format(new XmlFormatter(), "t.xml", xml, DocumentKind.Xml));
    }

    // ---- Files that are not UTF-8 are not read (their bytes would be lost by formatting)

    [Fact]
    public void Latin1_IsNotRead_AndTheLineIsTold()
    {
        var bytes = Encoding.Latin1.GetBytes("p {\n  content: \"é\";\n}\n");
        var e = Assert.Throws<NotUtf8Exception>(() => DocumentIo.FromBytes("a.css", bytes));
        Assert.Equal(2, e.Line);
        Assert.Contains("0xE9", e.Message, StringComparison.Ordinal);
        Assert.Equal("file.not-utf8", e.ToDiagnostic("a.css").Code);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFE, (byte)'a', 0 }, "UTF-16")]
    [InlineData(new byte[] { 0xFE, 0xFF, 0, (byte)'a' }, "UTF-16")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0, 0, (byte)'a', 0, 0, 0 }, "UTF-32")]
    public void Utf16AndUtf32_AreNotRead(byte[] bytes, string encoding)
    {
        var e = Assert.Throws<NotUtf8Exception>(() => DocumentIo.FromBytes("a.html", bytes));
        Assert.Contains(encoding, e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Utf8_WithAndWithoutBom_IsRead()
    {
        var text = "<p>Blåbærgrød – “quotes” 🙂</p>";
        var plain = DocumentIo.FromBytes("a.html", Encoding.UTF8.GetBytes(text));
        Assert.Equal(text, plain.Content);
        Assert.False(plain.HasUtf8Bom);
        var withBom = DocumentIo.FromBytes("a.html", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)]);
        Assert.Equal(text, withBom.Content);
        Assert.True(withBom.HasUtf8Bom);
    }

    [Fact]
    public async Task ResultStore_ReportsAFileThatIsNotUtf8()
    {
        var path = Path.Combine(_dir, "old.html");
        await File.WriteAllBytesAsync(path, Encoding.Latin1.GetBytes("<p>café</p>"));
        using var store = new ResultStore("native", new VnuOptions());
        store.Get(path);
        await store.WaitIdleAsync(TimeSpan.FromSeconds(10));
        var result = store.Get(path)!;
        Assert.Null(result.Failure);
        Assert.Equal("file.not-utf8", Assert.Single(result.Diagnostics).Code);
    }

    // ---- Line endings

    [Theory]
    [InlineData("a\rb\rc\r", "\r")]
    [InlineData("a\r\nb\r\n", "\r\n")]
    [InlineData("a\nb\n", "\n")]
    [InlineData("a\r\nb\nc\nd\n", "\n")]
    [InlineData("a\r\nb\r\nc\n", "\r\n")]
    [InlineData("no breaks", "\n")]
    public void TheLineEndingMostlyUsed_IsKept(string text, string expected)
    {
        Assert.Equal(expected, new SourceDocument("a.css", text, DocumentKind.Css).DetectNewLine());
    }

    [Fact]
    public void OldMacLineEndings_StayOldMac()
    {
        var formatted = Format(new CssFormatter(), "a.css", "a{color:red}\rb{margin:0}\r", DocumentKind.Css);
        Assert.DoesNotContain('\n', formatted);
        Assert.Contains("\r", formatted, StringComparison.Ordinal);
    }

    // ---- CSS formatting changes whitespace only

    [Theory]
    [InlineData("a { color /* why */ : red }", "color /* why */: red;")]
    [InlineData("a { margin: 0 !important /* keep */ }", "margin: 0 !important /* keep */;")]
    [InlineData("a { padding: 0 ! /*x*/ important }", "padding: 0 ! /*x*/ important;")]
    [InlineData("a { color: red !IMPORTANT }", "color: red !IMPORTANT;")]
    [InlineData("a { --my\\:var: 1 }", "--my\\:var: 1;")]
    [InlineData("a { col\\6fr: red }", "col\\6fr: red;")]
    [InlineData("@\\media print { a { color: red } }", "@\\media print {")]
    public void CssFormatting_KeepsCommentsEscapesAndCase(string css, string expected)
    {
        Assert.Contains(expected, Format(new CssFormatter(), "a.css", css, DocumentKind.Css), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a { color /* a */ : red /* b */ !important /* c */; margin:0 } /* d */ b { --x : { y } }")]
    [InlineData("@media /* m */ screen { a::before { content: \"}\" /* e */ } }")]
    public void CssFormatting_KeepsEveryTokenButWhitespace(string css)
    {
        static List<string> Tokens(string s) =>
            [.. CssTokenizer.Tokenize(s, out _).Where(t => t.Type is not (CssTokenType.Whitespace or CssTokenType.EndOfFile)).Select(t => t.Text(s))];
        var formatted = Format(new CssFormatter(), "a.css", css, DocumentKind.Css);
        // The same tokens, comments included (a ";" may be added after the last declaration of a block)
        Assert.Equal(Tokens(css).Where(t => t != ";"), Tokens(formatted).Where(t => t != ";"));
    }

    [Fact]
    public void CssWithABadString_IsNotFormatted()
    {
        var e = Assert.Throws<Core.FormatException>(() =>
            Format(new CssFormatter(), "a.css", "a { content: \"abc\n  color: red; margin: 0 }", DocumentKind.Css));
        Assert.Contains("line 1", e.Message, StringComparison.Ordinal);
    }

    // ---- HTML formatting changes whitespace only where it does not show

    [Fact]
    public void HiddenElements_InTheMiddleOfText_StayInTheLine()
    {
        var formatted = Format(new HtmlFormatter(), "t.html",
            "<p>Price:<script>document.write(5)</script>EUR and <span>a</span><noscript>b</noscript><span>c</span></p>", DocumentKind.Html);
        Assert.Contains("Price:<script>document.write(5)</script>EUR", formatted, StringComparison.Ordinal);
        Assert.Contains("<span>a</span><noscript>b</noscript><span>c</span>", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenElements_BetweenWhitespace_GetLinesOfTheirOwn()
    {
        var formatted = Format(new HtmlFormatter(), "t.html",
            "<head><meta charset=\"utf-8\"><title>T</title><link rel=\"stylesheet\" href=\"a.css\"></head><p>Text <script>x()</script> more</p>",
            DocumentKind.Html).ReplaceLineEndings("\n");
        Assert.Contains("\n  <meta charset=\"utf-8\">\n", formatted, StringComparison.Ordinal);
        Assert.Contains("\n  <script>x()</script>\n", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlInForeignObject_IsFormattedAsHtml()
    {
        var formatted = Format(new HtmlFormatter(), "t.html",
            "<svg viewBox=\"0 0 9 9\"><foreignObject width=\"9\" height=\"9\"><div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Hello <b>World</b>!</p></div></foreignObject></svg>",
            DocumentKind.Html);
        Assert.Contains("<p>Hello <b>World</b>!</p>", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HtmlInForeignObject_IsCheckedAsHtml()
    {
        const string html = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body><svg viewBox=\"0 0 9 9\"><foreignObject width=\"9\" height=\"9\">" +
                            "<div xmlns=\"http://www.w3.org/1999/xhtml\"><img src=\"a.png\"></div></foreignObject></svg>";
        var problems = await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html, DocumentKind.Html));
        Assert.Equal("html.img-missing-alt", Assert.Single(problems).Code);
    }

    [Fact]
    public async Task ANoBreakSpace_IsPartOfAnUnquotedValue()
    {
        const string html = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body><a href=/a b>x</a>";
        Assert.Empty(await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html, DocumentKind.Html)));
        Assert.Contains("href=/a b>", Format(new HtmlFormatter(), "t.html", html, DocumentKind.Html), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<p title=\"unclosed>text</p>")]
    [InlineData("<p>Text</p><img src=\"a.png\"")]
    public void HtmlEndingInsideATag_IsNotFormatted(string html)
    {
        Assert.Throws<Core.FormatException>(() => Format(new HtmlFormatter(), "t.html", html, DocumentKind.Html));
    }

    // ---- XML formatting keeps text where whitespace between elements shows

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><text x=\"0\" y=\"9\"><tspan>a</tspan><tspan>b</tspan></text></svg>",
        "<text x=\"0\" y=\"9\"><tspan>a</tspan><tspan>b</tspan></text>")]
    [InlineData("<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><content type=\"xhtml\"><div xmlns=\"http://www.w3.org/1999/xhtml\"><p><b>a</b><i>b</i></p></div></content></entry></feed>",
        "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p><b>a</b><i>b</i></p></div>")]
    [InlineData("<list><sep> </sep></list>", "<sep> </sep>")]
    [InlineData("<doc xml:space=\"preserve\"><a>1</a><b>2</b></doc>", "<doc xml:space=\"preserve\"><a>1</a><b>2</b></doc>")]
    public void XmlWhereWhitespaceShows_IsKeptAsWritten(string xml, string kept)
    {
        Assert.Contains(kept, Format(new XmlFormatter(), "t.xml", xml, DocumentKind.Xml), StringComparison.Ordinal);
    }

    [Fact]
    public void XmlData_IsStillIndented()
    {
        var formatted = Format(new XmlFormatter(), "s.xml", "<urlset><url><loc>https://a.example/</loc></url></urlset>", DocumentKind.Xml);
        Assert.Equal("<urlset>\n  <url>\n    <loc>https://a.example/</loc>\n  </url>\n</urlset>\n", formatted);
    }

    // ---- Collecting files

    [Fact]
    public void FileCollector_IgnoresCase_DoesEachFileOnce_AndCountsMissingPaths()
    {
        var sub = Path.Combine(_dir, "site");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "INDEX.HTML"), "<p>x</p>");
        File.WriteAllText(Path.Combine(sub, "README.MD"), "# x");
        File.WriteAllText(Path.Combine(sub, "notes.txt"), "x");

        var errors = new StringWriter();
        var files = FileCollector.Collect([sub, Path.Combine(sub, "INDEX.HTML"), Path.Combine(_dir, "missing.html")], false, errors,
            out var missing);
        Assert.Equal(2, files.Count);
        Assert.Equal(1, missing);
        Assert.Contains("missing.html", errors.ToString(), StringComparison.Ordinal);
    }
}
