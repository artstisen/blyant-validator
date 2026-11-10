using Blyant.Validator.Cli;
using Blyant.Validator.Cli.Tui;
using Blyant.Validator.Core;
using Blyant.Validator.Html;
using Blyant.Validator.Vnu;

namespace Blyant.Validator.Tests;

/// <summary>Fragments (HTML for a page made elsewhere, as a CMS article), ignored codes, and the rules per folder.</summary>
public sealed class FragmentAndIgnoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blyant-validator-fragment-" + Guid.NewGuid().ToString("N"));

    public FragmentAndIgnoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private const string Article = """
        <h2>Getting started</h2>
        <p>Install it with <code>dotnet tool install</code>.</p>
        <style>
          .note { border-left: 4px solid #369; padding: .5em 1em; }
        </style>
        <div class="note" style="color: #333">A note.</div>
        <pre><code>&lt;p&gt;Hello&lt;/p&gt;</code></pre>
        <script>
          document.querySelector('.note').title = 'Hi';
        </script>
        """;

    private static async Task<IReadOnlyList<Diagnostic>> Check(string html, bool fragment) =>
        await new HtmlValidator(fragment).ValidateAsync(new SourceDocument("t.html", html.ReplaceLineEndings("\n"), DocumentKind.Html));

    [Fact]
    public async Task Article_HasPageProblems_AsAPage()
    {
        var codes = (await Check(Article, fragment: false)).Select(d => d.Code).ToList();
        Assert.Contains("html.parse.missing-doctype", codes);
        Assert.Contains("html.head-missing-title", codes);
        Assert.Contains("html.missing-lang", codes);
    }

    [Fact]
    public async Task Article_HasNoProblems_AsAFragment()
    {
        Assert.Empty(await Check(Article, fragment: true));
    }

    [Fact]
    public async Task Fragment_StillChecksItsContent()
    {
        var problems = await Check("<p style=\"colr: red\">x</p><img src=\"a.png\"><p id=\"a\"></p><p id=\"a\"></p>", fragment: true);
        Assert.Contains(problems, d => d.Code == "css.unknown-property");
        Assert.Contains(problems, d => d.Code == "html.img-missing-alt");
        Assert.Contains(problems, d => d.Code == "html.duplicate-id");
    }

    [Theory]
    [InlineData("<!DOCTYPE html><p>x</p>")]
    [InlineData("<html><p>x</p></html>")]
    [InlineData("<head><meta charset=\"utf-8\"></head><p>x</p>")]
    [InlineData("<body><p>x</p></body>")]
    [InlineData("<title>Page</title><p>x</p>")]
    [InlineData("<meta name=\"description\" content=\"x\"><p>x</p>")]
    public async Task PageParts_InAFragment_AreWarnings(string html)
    {
        var problems = await Check(html, fragment: true);
        Assert.NotEmpty(problems);
        Assert.All(problems.Where(d => d.Code == "html.fragment-page-part"), d => Assert.Equal(Severity.Warning, d.Severity));
        Assert.Contains(problems, d => d.Code == "html.fragment-page-part");
    }

    [Fact]
    public async Task MicrodataMeta_AndSvgTitle_AreFineInAFragment()
    {
        Assert.Empty(await Check(
            "<div itemscope><meta itemprop=\"name\" content=\"x\"></div><svg viewBox=\"0 0 1 1\"><title>Icon</title></svg>", fragment: true));
    }

    [Theory]
    [InlineData("html.missing-lang", true)]
    [InlineData("css.*", true)]
    [InlineData("html.parse.*", true)]
    [InlineData("vnu.error", true)]
    [InlineData("*", false)]
    [InlineData("css*", false)]
    [InlineData("html missing", false)]
    [InlineData("", false)]
    public void IgnoreEntries_AreCodesOrPrefixes(string entry, bool valid)
    {
        Assert.Equal(valid, IgnoreList.IsValid(entry));
    }

    [Fact]
    public void IgnoreList_MatchesCodesAndPrefixes()
    {
        Assert.Equal(["html.missing-lang", "css.*"], IgnoreList.Split("HTML.missing-lang, css.*,css.*"));
        Assert.True(IgnoreList.Matches("css.unknown-property", ["css.*"]));
        Assert.True(IgnoreList.Matches("html.parse.missing-doctype", ["html.parse.*"]));
        Assert.False(IgnoreList.Matches("html.missing-lang", ["html.parse.*"]));
        Assert.False(IgnoreList.Matches("cssx.a", ["css.*"]));
        Assert.True(IgnoreList.Matches("html.missing-lang", ["html.missing-lang"]));
        Assert.False(IgnoreList.Matches("html.missing-lang-x", ["html.missing-lang"]));
    }

    [Fact]
    public void FolderRules_ApplyToSubfolders_AndKeepOnlyDifferences()
    {
        var docs = Path.Combine(_dir, "docs");
        var sub = Path.Combine(docs, "guides");
        var rules = new FolderRules();
        Assert.True(rules.ForDirectory(sub).IsFull);

        rules.Set(docs, new CheckRules(true, ["css.*"]));
        Assert.True(rules.ForDirectory(sub).Fragment);
        Assert.True(rules.ForFile(Path.Combine(sub, "a.html")).Fragment);
        Assert.True(rules.ForDirectory(_dir).IsFull);

        // A subfolder with rules of its own, and back to those of the folder above: not kept
        rules.Set(sub, new CheckRules(false, ["css.*"]));
        Assert.False(rules.ForDirectory(sub).Fragment);
        Assert.Equal(2, rules.Folders.Count);
        rules.Set(sub, new CheckRules(true, ["css.*"]));
        Assert.Single(rules.Folders);

        rules.Set(docs + Path.DirectorySeparatorChar, CheckRules.Full);
        Assert.Empty(rules.Folders);
    }

    [Fact]
    public void FolderRules_LeaveOutCodesThatAreNotValid()
    {
        var read = FolderRules.Read(new Dictionary<string, string>
        {
            ["checkdir1"] = _dir,
            ["checkfragment1"] = "TRUE",
            ["checkignore1"] = "*,css.*,bad!code,html.x",
        });
        Assert.Equal(new CheckRules(true, ["css.*", "html.x"]), read.ForDirectory(_dir));
    }

    [Fact]
    public async Task ResultStore_UsesTheRulesOfTheFolder()
    {
        var path = Path.Combine(_dir, "article.html");
        await File.WriteAllTextAsync(path, "<h2>Title</h2>\n<p style=\"colr: red\">Text</p>\n");
        using var store = new ResultStore("native", new VnuOptions());

        store.Get(path);
        await store.WaitIdleAsync(TimeSpan.FromSeconds(10));
        var page = store.Get(path)!;
        Assert.False(page.Fragment);
        Assert.Equal(3, page.Errors); // The doctype, the title, and the CSS

        store.SetRules(_dir, new CheckRules(true, ["css.unknown-property"]));
        Assert.Null(store.Get(path));
        await store.WaitIdleAsync(TimeSpan.FromSeconds(10));
        var fragment = store.Get(path)!;
        Assert.True(fragment.Fragment);
        Assert.Equal(1, fragment.Ignored);
        Assert.Empty(fragment.Diagnostics);
    }

    [Fact]
    public void Badge_TellsWhatIsNotChecked()
    {
        Assert.Equal(string.Empty, Browser.Badge(CheckRules.Full));
        Assert.Equal(" FRAGMENT ", Browser.Badge(new CheckRules(true, [])));
        Assert.Equal(" FRAGMENT  IGNORE 2 ", Browser.Badge(new CheckRules(true, ["a", "b"])));
    }

    [Fact]
    public void RepeatedOption_KeepsAllItsValues()
    {
        var spec = new CommandSpec("validate", "", [new("--ignore", "-i", "", OptionKind.Text, Repeatable: true)], "<paths>");
        var parsed = CommandLine.Parse(spec, ["-i", "a.b", "--ignore=c.*", "x.html"]);
        Assert.Equal(["a.b", "c.*"], parsed.Texts("--ignore"));
        Assert.Equal(["x.html"], parsed.Arguments);
    }
}
