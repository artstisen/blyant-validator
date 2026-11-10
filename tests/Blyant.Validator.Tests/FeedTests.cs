using Blyant.Validator.Core;
using Blyant.Validator.Xml;

namespace Blyant.Validator.Tests;

public class FeedTests
{
    // (e-mail addresses are made here, so that none is written in the files of the repository: AGENTS.md)
    private const string At = "@";
    private static readonly string Editor = "editor" + At + "harbour-lights.example (The Editor)";

    private static IReadOnlyList<Diagnostic> Validate(string xml) => XmlValidator.Validate("feed.xml", xml.ReplaceLineEndings("\n"));

    private static string Rss(string channelExtra = "", string item = "") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0" xmlns:atom="http://www.w3.org/2005/Atom" xmlns:dc="http://purl.org/dc/elements/1.1/">
          <channel>
            <title>Harbour Lights</title>
            <link>https://harbour-lights.example/</link>
            <description>News of the book</description>
            <atom:link href="https://harbour-lights.example/feed.xml" rel="self" type="application/rss+xml"/>
            {channelExtra}
            <item>
              <title>The second draft is done</title>
              <link>https://harbour-lights.example/news/draft.html</link>
              <guid>https://harbour-lights.example/news/draft.html</guid>
              <pubDate>Tue, 10 Nov 2026 14:30:00 GMT</pubDate>
              <dc:creator>Maren Holt</dc:creator>
            </item>
            {item}
          </channel>
        </rss>
        """;

    [Fact]
    public void ValidRss_HasNoProblems()
    {
        var extra = $"<language>en-gb</language><managingEditor>{Editor}</managingEditor>" +
                    "<lastBuildDate>10 Nov 2026 14:30 +0100</lastBuildDate><ttl>60</ttl>" +
                    "<image><url>https://harbour-lights.example/logo.png</url><title>Harbour Lights</title><link>https://harbour-lights.example/</link></image>" +
                    "<skipHours><hour>0</hour><hour>23</hour></skipHours><skipDays><day>Sunday</day></skipDays>";
        var item = "<item><description>A reading at the harbour office.</description>" +
                   "<guid isPermaLink=\"false\">reading-2026-11</guid>" +
                   "<enclosure url=\"https://harbour-lights.example/reading.mp3\" length=\"123456\" type=\"audio/mpeg\"/>" +
                   "<category>Events</category><category>Readings</category></item>";
        Assert.Empty(Validate(Rss(extra, item)));
    }

    [Theory]
    [InlineData("", "<item><link>https://a.org/</link></item>", "rss.missing-element")]
    [InlineData("<pubDate>2026-11-10</pubDate>", "", "rss.bad-date")]
    [InlineData("<pubDate>Tue, 31 Nov 2026 14:30:00 GMT</pubDate>", "", "rss.bad-date")]
    [InlineData("<docs>/rss-spec</docs>", "", "rss.bad-url")]
    [InlineData("", "<item><title>x</title><guid>not-a-url</guid></item>", "rss.bad-guid")]
    [InlineData("", "<item><title>x</title><guid>https://harbour-lights.example/news/draft.html</guid></item>", "rss.duplicate-guid")]
    [InlineData("", "<item><title>x</title><enclosure url=\"https://a.org/a.mp3\" type=\"audio/mpeg\"/></item>", "rss.missing-attribute")]
    [InlineData("<ttl>an hour</ttl>", "", "rss.bad-value")]
    [InlineData("<skipDays><day>Sun</day></skipDays>", "", "rss.bad-value")]
    [InlineData("<title>Again</title>", "", "rss.duplicate-element")]
    [InlineData("<author>Maren</author>", "", "rss.unknown-element")]
    [InlineData("<managingEditor>Maren Holt</managingEditor>", "", "rss.bad-value")]
    public void RssProblem_IsReported(string channelExtra, string item, string code)
    {
        Assert.Contains(Validate(Rss(channelExtra, item)), d => d.Code == code);
    }

    [Fact]
    public void RssWrongWeekday_IsAWarning()
    {
        var d = Assert.Single(Validate(Rss("<pubDate>Mon, 10 Nov 2026 14:30:00 GMT</pubDate>")));
        Assert.Equal(("rss.bad-date", Severity.Warning), (d.Code, d.Severity));
    }

    [Fact]
    public void RssWithoutVersionOrChannel_IsAnError()
    {
        Assert.Contains(Validate("<rss><channel><title>t</title><link>https://a.org/</link><description>d</description></channel></rss>"),
            d => d.Code == "rss.version");
        Assert.Contains(Validate("<rss version=\"2.0\"></rss>"), d => d.Code == "rss.channel");
    }

    private static string Atom(string feedExtra = "", string entry = "") => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <feed xmlns="http://www.w3.org/2005/Atom">
          <title>Harbour Lights</title>
          <link href="https://harbour-lights.example/"/>
          <link rel="self" href="https://harbour-lights.example/feed.atom"/>
          <updated>2026-11-10T14:30:00Z</updated>
          <id>urn:uuid:60a76c80-d399-11d9-b93C-0003939e0af6</id>
          {feedExtra}
          <entry>
            <title>The second draft is done</title>
            <link href="https://harbour-lights.example/news/draft.html"/>
            <id>tag:harbour-lights.example,2026:draft</id>
            <updated>2026-11-10T14:30:00+01:00</updated>
            <summary>Done at last.</summary>
          </entry>
          {entry}
        </feed>
        """;

    [Fact]
    public void ValidAtom_HasNoProblems()
    {
        var entry = "<entry><title>A reading</title><id>https://harbour-lights.example/reading</id>" +
                    "<updated>2026-11-12T09:00:00.5Z</updated><content type=\"html\">&lt;p&gt;Come!&lt;/p&gt;</content></entry>";
        Assert.Empty(Validate(Atom("<author><name>Maren Holt</name></author>", entry)));
    }

    [Theory]
    [InlineData("", "", "atom.missing-author")]
    [InlineData("<author><name>M</name></author>", "<entry><title>x</title><id>urn:x</id><updated>2026-11-10T14:30:00Z</updated></entry>", "atom.missing-content")]
    [InlineData("<author><name>M</name></author>", "<entry><title>x</title><id>urn:y</id><updated>2026-11-10</updated><content>x</content></entry>", "atom.bad-date")]
    [InlineData("<author><name>M</name></author>", "<entry><title>x</title><id>just-text</id><updated>2026-11-10T14:30:00Z</updated><content>x</content></entry>", "atom.bad-id")]
    [InlineData("<author><name>M</name></author>", "<entry><title>x</title><updated>2026-11-10T14:30:00Z</updated><content>x</content></entry>", "atom.missing-element")]
    [InlineData("<author><name>M</name></author><title>Again</title>", "", "atom.duplicate-element")]
    [InlineData("<author><email>no-name</email></author>", "", "atom.missing-element")]
    [InlineData("<author><name>M</name></author>", "<entry><title>x</title><id>tag:harbour-lights.example,2026:draft</id><updated>2026-11-10T14:30:00Z</updated><content>x</content></entry>", "atom.duplicate-id")]
    [InlineData("<author><name>M</name></author><link rel=\"related\"/>", "", "atom.missing-attribute")]
    public void AtomProblem_IsReported(string feedExtra, string entry, string code)
    {
        Assert.Contains(Validate(Atom(feedExtra, entry)), d => d.Code == code);
    }

    [Theory]
    [InlineData("feed.rss")]
    [InlineData("news.atom")]
    public void FeedExtensions_AreXml(string name)
    {
        Assert.Equal(DocumentKind.Xml, SourceDocument.DetectKind(name));
    }
}
