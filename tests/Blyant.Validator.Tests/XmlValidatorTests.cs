using Blyant.Validator.Core;
using Blyant.Validator.Xml;

namespace Blyant.Validator.Tests;

public sealed class XmlValidatorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blyant-validator-xml-" + Guid.NewGuid().ToString("N"));

    public XmlValidatorTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static IReadOnlyList<Diagnostic> Validate(string xml) => XmlValidator.Validate("t.xml", xml.ReplaceLineEndings("\n"));

    private const string Urlset = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n";

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><root><a>1</a><b x=\"y\"/></root>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><circle cx=\"5\" cy=\"5\" r=\"4\"/></svg>")]
    [InlineData("<catalog><book id=\"b1\"><title>A &amp; B</title></book></catalog>")]
    [InlineData("<!DOCTYPE note SYSTEM \"note.dtd\"><note>x</note>")]
    public void WellFormed_HasNoProblems(string xml)
    {
        Assert.Empty(Validate(xml));
    }

    [Theory]
    [InlineData("<root><a></root>", 1, 12)]
    [InlineData("<root>\n  <a x=\"1\" x=\"2\"/>\n</root>", 2, 12)]
    [InlineData("<root>Fish & chips</root>", 1, 13)]
    [InlineData("<root></root><other/>", 1, 15)]
    [InlineData("<root>\n<b>x</c>\n</root>", 2, 7)]
    public void NotWellFormed_IsOneErrorAtItsPlace(string xml, int line, int column)
    {
        var d = Assert.Single(Validate(xml));
        Assert.Equal("xml.not-well-formed", d.Code);
        Assert.Equal(new SourcePosition(line, column), d.Start);
        Assert.DoesNotContain("Line ", d.Message);
    }

    [Theory]
    [InlineData("<r>Fish & chips</r>", "write “&amp;”")]
    [InlineData("<r>a&nbsp;b</r>", "&#160;")]
    public void CommonMistakes_AreToldInPlainWords(string xml, string hint)
    {
        Assert.Contains(hint, Assert.Single(Validate(xml)).Message);
    }

    [Fact]
    public void ExternalEntities_AreNotRead()
    {
        // An entity that points at a file: the DTD is ignored, so the entity is unknown (an error), and never read
        var secret = Path.Combine(_dir, "secret.txt");
        File.WriteAllText(secret, "SECRET");
        var xml = $"<!DOCTYPE r [<!ENTITY s SYSTEM \"file:///{secret.Replace('\\', '/')}\">]><r>&s;</r>";

        var result = Validate(xml);
        Assert.All(result, d => Assert.DoesNotContain("SECRET", d.Message));
        Assert.Contains(result, d => d.Code == "xml.not-well-formed");
    }

    [Fact]
    public void ValidSitemap_WithExtensions_HasNoProblems()
    {
        var xml = Urlset.Replace("\">", "\" xmlns:image=\"http://www.google.com/schemas/sitemap-image/1.1\" xmlns:xhtml=\"http://www.w3.org/1999/xhtml\">") + """
              <url>
                <loc>https://example.org/</loc>
                <lastmod>2026-11-10</lastmod>
                <changefreq>weekly</changefreq>
                <priority>1.0</priority>
                <image:image><image:loc>https://example.org/cover.jpg</image:loc></image:image>
                <xhtml:link rel="alternate" hreflang="da" href="https://example.org/da/"/>
              </url>
              <url><loc>https://example.org/about.html?lang=en&amp;page=2</loc><lastmod>2026-11-10T14:30:00+01:00</lastmod><priority>0.5</priority></url>
              <url><loc>https://example.org/news</loc><lastmod>2026-11</lastmod></url>
            </urlset>
            """;
        Assert.Empty(Validate(xml));
    }

    [Fact]
    public void ValidSitemapIndex_HasNoProblems()
    {
        Assert.Empty(Validate("""
            <?xml version="1.0" encoding="UTF-8"?>
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap><loc>https://example.org/sitemap-1.xml</loc><lastmod>2026-11-10T14:30:00Z</lastmod></sitemap>
              <sitemap><loc>https://example.org/sitemap-2.xml.gz</loc></sitemap>
            </sitemapindex>
            """));
    }

    [Theory]
    [InlineData("<url><lastmod>2026-01-01</lastmod></url>", "sitemap.missing-loc")]
    [InlineData("<url><loc>/about.html</loc></url>", "sitemap.bad-loc")]
    [InlineData("<url><loc>ftp://example.org/</loc></url>", "sitemap.bad-loc")]
    [InlineData("<url><loc>https://example.org/my page</loc></url>", "sitemap.bad-loc")]
    [InlineData("<url><loc>https://example.org/</loc><lastmod>10/11/2026</lastmod></url>", "sitemap.bad-lastmod")]
    [InlineData("<url><loc>https://example.org/</loc><lastmod>2026-02-30</lastmod></url>", "sitemap.bad-lastmod")]
    [InlineData("<url><loc>https://example.org/</loc><lastmod>2026-11-10T14:30</lastmod></url>", "sitemap.bad-lastmod")]
    [InlineData("<url><loc>https://example.org/</loc><changefreq>sometimes</changefreq></url>", "sitemap.bad-changefreq")]
    [InlineData("<url><loc>https://example.org/</loc><priority>2</priority></url>", "sitemap.bad-priority")]
    [InlineData("<url><loc>https://example.org/</loc><priority>high</priority></url>", "sitemap.bad-priority")]
    [InlineData("<url><loc>https://example.org/</loc><loc>https://example.org/b</loc></url>", "sitemap.duplicate-element")]
    [InlineData("<url><loc>https://example.org/</loc><title>Home</title></url>", "sitemap.unknown-element")]
    [InlineData("<page><loc>https://example.org/</loc></page>", "sitemap.unknown-element")]
    [InlineData("<url><loc>https://example.org/</loc></url><url><loc>https://example.org/</loc></url>", "sitemap.duplicate-loc")]
    [InlineData("<url><loc>https://example.org/</loc></url><url><loc>https://other.org/</loc></url>", "sitemap.mixed-hosts")]
    [InlineData("", "sitemap.empty")]
    public void SitemapProblem_IsReported(string entries, string code)
    {
        Assert.Contains(Validate(Urlset + entries + "\n</urlset>"), d => d.Code == code);
    }

    [Fact]
    public void Sitemap_InTheWrongNamespace_IsAnError()
    {
        var d = Assert.Single(Validate("<urlset><url><loc>https://example.org/</loc></url></urlset>"));
        Assert.Equal("sitemap.namespace", d.Code);
    }

    [Fact]
    public void Sitemap_NotInUtf8_IsAnError()
    {
        Assert.Contains(Validate(Urlset.Replace("UTF-8", "ISO-8859-1") + "<url><loc>https://example.org/</loc></url></urlset>"),
            d => d.Code == "sitemap.encoding");
    }

    [Fact]
    public void SitemapProblem_PointsAtTheElement()
    {
        var d = Assert.Single(Validate(Urlset + "  <url>\n    <loc>https://example.org/</loc>\n    <priority>9</priority>\n  </url>\n</urlset>"));
        Assert.Equal(new SourcePosition(5, 6), d.Start);
    }

    [Fact]
    public void TooManyEntries_IsAnError()
    {
        var entries = string.Concat(Enumerable.Range(0, 50_001).Select(i => $"<url><loc>https://example.org/{i}</loc></url>"));
        Assert.Contains(Validate(Urlset + entries + "</urlset>"), d => d.Code == "sitemap.too-many-entries");
    }
}
