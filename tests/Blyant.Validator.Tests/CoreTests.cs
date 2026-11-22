using Blyant.Validator.Core;

namespace Blyant.Validator.Tests;

/// <summary>The helpers the engines share (Blyant.Validator.Core).</summary>
public sealed class CoreTests
{
    [Fact]
    public void LineBreaks_AreCrLfLfAndCr_NotTheOtherBreaksOfUnicode()
    {
        const string text = "a\r\nb\nc\rd\fe\u0085f\u2028g\u2029h";
        Assert.Equal("a|b|c|d\fe\u0085f\u2028g\u2029h", LineBreaks.Normalize(text, "|"));
        Assert.Equal(3, LineBreaks.Count(text));
        Assert.Equal(["a", "b", "c", "d\fe\u0085f\u2028g\u2029h"], LineBreaks.Split(text));
        Assert.Equal(["a", ""], LineBreaks.Split("a\n"));
    }

    [Theory]
    [InlineData("https://example.org/page.html", true)]
    [InlineData("http://example.org", true)]
    [InlineData("ftp://example.org/file", false)]
    [InlineData("https:///page.html", false)]
    [InlineData("/page.html", false)]
    [InlineData("example.org", false)]
    public void AbsoluteHttpUrls_HaveTheSchemeAndAHost(string url, bool absolute)
    {
        Assert.Equal(absolute, Urls.IsAbsoluteHttp(url));
    }

    [Theory]
    [InlineData("abc", 5, "abc")]
    [InlineData("abcdef", 5, "abcde…")]
    [InlineData("abcd😀", 5, "abcd…")]
    public void Shorten_CutsBeforeACharacterOfTwoUnits(string text, int max, string shortened)
    {
        Assert.Equal(shortened, Texts.Shorten(text, max));
    }
}
