using System.Text;
using Blyant.Validator.Cli;
using Blyant.Validator.Cli.Tui;
using Blyant.Validator.Cli.Tui.Terminal;
using Blyant.Validator.Core;
using Blyant.Validator.Html;
using Blyant.Validator.Vnu;

namespace Blyant.Validator.Tests;

/// <summary>The TUI, vnu and the command line in the cases found in the code review.</summary>
public sealed class RobustnessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blyant-validator-robust-" + Guid.NewGuid().ToString("N"));

    public RobustnessTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    // ---- Results

    [Fact]
    public async Task ResultStore_UsesTheRulesSetWhileAFileIsChecked()
    {
        var path = Path.Combine(_dir, "a.html");
        await File.WriteAllTextAsync(path, "<h2>Title</h2>");
        using var store = new ResultStore("native", new VnuOptions());
        store.Get(path);
        store.SetRules(_dir, new CheckRules(true, []));
        // (a result made before the change is not kept: the file is checked again, with the new rules)
        await store.WaitIdleAsync(TimeSpan.FromSeconds(10));
        store.Get(path);
        await store.WaitIdleAsync(TimeSpan.FromSeconds(10));
        var result = store.Get(path)!;
        Assert.True(result.Fragment);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ResultStore_DoesNotAskForAFileThatIsGone()
    {
        using var store = new ResultStore("native", new VnuOptions());
        var path = Path.Combine(_dir, "gone.html");
        Assert.Null(store.Get(path));
        Assert.False(store.IsPending(path));
    }

    // ---- The peek

    [Fact]
    public void PeekDocument_OfExactlyTheMostLines_IsNotCut()
    {
        var text = string.Concat(Enumerable.Repeat("x\n", PeekDocument.MaxLines));
        var document = new PeekDocument("t.txt", text, []);
        Assert.False(document.IsCut);
        Assert.Equal(PeekDocument.MaxLines, document.Lines.Count);
    }

    [Fact]
    public void PeekDocument_ShowsTheStartOfAVeryLongLine()
    {
        var document = new PeekDocument("min.css", new string('a', PeekDocument.MaxCharacters + 10), []);
        Assert.True(document.IsCut);
        Assert.Equal(PeekDocument.MaxCharacters, Assert.Single(document.Lines).Length);
        Assert.Null(document.StylesOf(0)); // (too long to be colored)
    }

    [Fact]
    public void PeekDocument_DoesNotPutProblemsAfterTheCutOnTheLastLine()
    {
        var problem = new Diagnostic("t.html", Severity.Error, "x", "after the cut", new(5, 1), new(5, 1));
        var document = new PeekDocument("t.html", "a\nb\nc", [problem], cut: true);
        Assert.Null(document.WorstOn(2));
        Assert.Single(document.Diagnostics);
    }

    [Fact]
    public async Task LoadStart_ReadsPart_AndDoesNotCutACharacter()
    {
        var path = Path.Combine(_dir, "big.html");
        await File.WriteAllTextAsync(path, "ab€cd", new UTF8Encoding(false)); // "€" is 3 bytes
        var start = DocumentIo.LoadStart(path, 4, out var cut);
        Assert.True(cut);
        Assert.Equal("ab", start.Content);
        var all = DocumentIo.LoadStart(path, 100, out cut);
        Assert.False(cut);
        Assert.Equal("ab€cd", all.Content);
    }

    // ---- Folder rules: a folder keeps only what it changes, so a change above reaches it

    [Fact]
    public void FolderRules_AChangeAbove_ReachesASubfolder()
    {
        var docs = Path.Combine(_dir, "docs");
        var sub = Path.Combine(docs, "guides");
        var rules = new FolderRules();
        rules.Set(docs, new CheckRules(false, ["css.*"]));
        rules.Set(sub, new CheckRules(true, ["css.*", "html.missing-lang"]));

        rules.Set(docs, new CheckRules(false, ["css.*", "xml.*"]));
        Assert.Equal(new CheckRules(true, ["css.*", "xml.*", "html.missing-lang"]), rules.ForDirectory(sub));
        rules.Set(docs, CheckRules.Full);
        Assert.Equal(new CheckRules(true, ["html.missing-lang"]), rules.ForDirectory(sub));
    }

    [Fact]
    public void FolderRules_UnignoreInASubfolder_IsKept()
    {
        var docs = Path.Combine(_dir, "docs");
        var sub = Path.Combine(docs, "raw");
        var rules = new FolderRules();
        rules.Set(docs, new CheckRules(true, ["css.*", "html.x"]));
        rules.Set(sub, new CheckRules(true, ["html.x"]));

        var read = FolderRules.Read(Settings.Parse(string.Join('\n', rules.Lines())));
        Assert.Equal(new CheckRules(true, ["html.x"]), read.ForDirectory(sub));
        Assert.Contains($"checkunignore2=css.*", rules.Lines());
    }

    // ---- The terminal

    [Fact]
    public void Printable_TakesOutBidiControls_AndKeepsTheWidth()
    {
        // (the characters are made from their numbers: written in the source, some would end the line)
        static string C(int code) => ((char)code).ToString();
        var name = "report" + C(0x202E) + "gnp.html";
        Assert.Equal("report gnp.html", TextMetrics.Printable(name));
        Assert.Equal(TextMetrics.Printable(name).Length, TextMetrics.WidthOf(name));
        Assert.Equal("a b c d", TextMetrics.Printable("a" + C(0x2066) + "b" + C(0x200F) + "c" + C(0x2028) + "d"));
        Assert.Equal("a b", TextMetrics.Printable("a" + C(0x1B) + "b"));
        var kept = "e" + C(0x301) + " " + C(0x200D);
        Assert.Equal(kept, TextMetrics.Printable(kept));
    }

    // ---- The command line

    [Fact]
    public void CommandLine_RefusesStdinAndAnOptionGivenTwice()
    {
        var spec = new CommandSpec("validate", "", [new("--output", "-o", "", OptionKind.Text, Choices: ["text", "json"])], "<paths>");
        Assert.Throws<UsageException>(() => CommandLine.Parse(spec, ["-"]));
        Assert.Throws<UsageException>(() => CommandLine.Parse(spec, ["-o", "json", "--output", "text", "a.html"]));
        Assert.Equal(["-"], CommandLine.Parse(spec, ["--", "-"]).Arguments);
    }

    [Fact]
    public void FileCollector_PassesOverDotFoldersAndNodeModules()
    {
        foreach (var folder in new[] { "site/css", "site/.git", "site/node_modules/pkg", "site/.cache" })
            Directory.CreateDirectory(Path.Combine(_dir, folder));
        File.WriteAllText(Path.Combine(_dir, "site", "index.html"), "x");
        File.WriteAllText(Path.Combine(_dir, "site", "css", "a.css"), "x");
        File.WriteAllText(Path.Combine(_dir, "site", ".git", "b.html"), "x");
        File.WriteAllText(Path.Combine(_dir, "site", "node_modules", "pkg", "README.md"), "x");
        File.WriteAllText(Path.Combine(_dir, "site", ".cache", "c.css"), "x");

        var files = FileCollector.Collect([Path.Combine(_dir, "site")], recursive: true, TextWriter.Null, out _);
        Assert.Equal(["a.css", "index.html"], files.Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SafeWrite_KeepsThePermissionsOrAttributes()
    {
        var path = Path.Combine(_dir, "run.html");
        File.WriteAllText(path, "old");
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            DocumentIo.SafeWriteAllText(path, "new", withBom: false);
            Assert.True(File.GetAttributes(path).HasFlag(FileAttributes.Hidden));
        }
        else
        {
            const UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead;
            File.SetUnixFileMode(path, mode);
            DocumentIo.SafeWriteAllText(path, "new", withBom: false);
            Assert.Equal(mode, File.GetUnixFileMode(path));
        }

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_dir)); // (no temporary file left)
    }

    [Fact]
    public void SafeWrite_WritesTheFileALinkLeadsTo()
    {
        var target = Path.Combine(_dir, "real.css");
        var link = Path.Combine(_dir, "link.css");
        File.WriteAllText(target, "old");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return; // (Windows without the right to make links)
        }

        DocumentIo.SafeWriteAllText(link, "new", withBom: false);
        Assert.Equal("new", File.ReadAllText(target));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }

    // ---- vnu's output

    [Theory]
    [InlineData("{\"messages\":{}}", 0)]
    [InlineData("[]", 0)]
    [InlineData("{\"messages\":[1,{\"message\":\"no type\",\"lastLine\":99999999999}]}", 1)]
    public void VnuOutput_OfAnUnexpectedShape_DoesNotThrow(string json, int count)
    {
        var result = VnuValidator.Parse("a.html", json);
        Assert.Equal(count, result.Count);
        Assert.All(result, d => Assert.Equal("vnu.message", d.Code));
    }

    // ---- HTML

    [Theory]
    [InlineData("<input type=\"url\" value=\"/path/page.html\">", true)]
    [InlineData("<input type=\"url\" value=\"https://a.example/\">", false)]
    public async Task UrlValues_AreTheSameOnEverySystem(string input, bool error)
    {
        var html = "<!DOCTYPE html><html lang=\"en\"><head><title>T</title></head><body>" + input;
        var problems = await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html, DocumentKind.Html));
        Assert.Equal(error, problems.Any(d => d.Code == "html.bad-value"));
    }

    [Fact]
    public async Task ADoctypeAtTheEnd_IsReadWhole()
    {
        var problems = await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", "<!DOCTYPE html", DocumentKind.Html));
        Assert.DoesNotContain(problems, d => d.Code == "html.parse.obsolete-doctype");
    }
}
