using Blyant.Validator.Cli;
using Blyant.Validator.Cli.Tui;
using Blyant.Validator.Cli.Tui.Terminal;
using Blyant.Validator.Core;
using Blyant.Validator.Vnu;

namespace Blyant.Validator.Tests;

public sealed class TuiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blyant-validator-tui-" + Guid.NewGuid().ToString("N"));

    public TuiTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Diagnostic At(int line, int column, Severity severity = Severity.Error) =>
        new("t.html", severity, "x", "message", new SourcePosition(line, column), new SourcePosition(line, column));

    [Fact]
    public void PeekDocument_MapsProblemsToLines()
    {
        var document = new PeekDocument("t.html", "<p>\r\n<b>\r\n<i>\r\n</p>\r\n",
            [At(2, 1, Severity.Warning), At(2, 3), At(4, 1, Severity.Info), At(0, 0)]);

        Assert.Equal(4, document.Lines.Count);
        Assert.Equal(Severity.Error, document.WorstOn(1));
        Assert.Equal(2, document.On(1).Count);
        Assert.Equal(Severity.Info, document.WorstOn(3));
        Assert.Null(document.WorstOn(2));
        // A problem without a line is shown on the first line
        Assert.Single(document.On(0));
    }

    [Fact]
    public void PeekDocument_FindsProblemsAndText_GoingRound()
    {
        var document = new PeekDocument("t.css", "a{}\nb{}\nc{}\nd{}", [At(2, 1), At(4, 1)]);

        Assert.Equal(1, document.NextProblemLine(0, true));
        Assert.Equal(3, document.NextProblemLine(1, true));
        Assert.Equal(1, document.NextProblemLine(3, true));
        Assert.Equal(3, document.NextProblemLine(1, false));
        Assert.Equal(2, document.Find("C{", 0, true));
        Assert.Null(document.Find("zzz", 0, true));
        Assert.NotNull(document.StylesOf(0));
    }

    [Fact]
    public void PeekDocument_CutsVeryLongFiles()
    {
        var text = string.Join('\n', Enumerable.Repeat("x", PeekDocument.MaxLines + 10));
        var document = new PeekDocument("t.html", text, []);
        Assert.True(document.IsCut);
        Assert.Equal(PeekDocument.MaxLines, document.Lines.Count);
    }

    [Fact]
    public async Task ResultStore_ValidatesInTheBackground_AndNoticesChanges()
    {
        var path = Path.Combine(_dir, "a.css");
        await File.WriteAllTextAsync(path, "a { colr: red }");
        using var store = new ResultStore(Engines.Native, new VnuOptions());

        Assert.Null(store.Get(path));
        await store.WaitIdleAsync(TimeSpan.FromSeconds(10));
        Assert.True(store.TakeChanged());
        var first = store.Get(path);
        Assert.NotNull(first);
        Assert.Equal(1, first.Errors);

        await File.WriteAllTextAsync(path, "a { color: red; margin: 0 }");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
        Assert.Null(store.Get(path));
        await store.WaitIdleAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(0, store.Get(path)!.Errors);
    }

    [Fact]
    public async Task ResultStore_TellsWhyAFileCouldNotBeChecked()
    {
        var path = Path.Combine(_dir, "a.html");
        await File.WriteAllTextAsync(path, "<p>x</p>");
        using var store = new ResultStore(new Engines(Vnu: true, Typst: false), new VnuOptions(Path.Combine(_dir, "missing.jar")));

        store.Get(path);
        await store.WaitIdleAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(store.Get(path)!.Failure);
    }

    [Fact]
    public void ResultStore_OnlyValidatesTheKindsItKnows()
    {
        Assert.True(ResultStore.IsValidated("a.HTML"));
        Assert.True(ResultStore.IsValidated("a.css"));
        Assert.False(ResultStore.IsValidated("a.txt"));
    }

    [Fact]
    public void CommandCompletion_CompletesNamesAndArguments()
    {
        var completion = new CommandCompletion(Browser.CommandTable);
        Assert.Equal("panes", completion.Next("pa", true));
        Assert.Equal("peek", new CommandCompletion(Browser.CommandTable).Next("pe", true));
        Assert.Equal("engine vnu", new CommandCompletion(Browser.CommandTable).Next("engine v", true));

        var cycle = new CommandCompletion(Browser.CommandTable);
        var a = cycle.Next("p", true);
        var b = cycle.Next(a, true);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Clipboard_SequenceIsOsc52WithBase64()
    {
        Assert.Equal("\u001b]52;c;QzpcU2l0ZXNcaW5kZXguaHRtbA==\u0007", Clipboard.Osc52Sequence(@"C:\Sites\index.html"));
    }

    [Theory]
    [InlineData("linux", null, null, true)]
    [InlineData("xterm-256color", "Apple_Terminal", null, true)]
    [InlineData("xterm-256color", null, "7600", true)]
    [InlineData("xterm-kitty", null, "7600", false)]
    [InlineData("xterm-256color", "WezTerm", "7600", false)]
    [InlineData("xterm-256color", null, null, false)]
    public void Clipboard_KnowsTerminalsWithoutIt(string term, string? program, string? vte, bool without)
    {
        string? Variable(string name) => name switch
        {
            "TERM" => term,
            "TERM_PROGRAM" => program,
            "VTE_VERSION" => vte,
            _ => null,
        };
        Assert.Equal(without, Clipboard.WithoutClipboard(Variable));
    }

    [Fact]
    public void CommandHistory_KeepsTheNewestFirst()
    {
        var history = new CommandHistory();
        history.Add("cd src");
        history.Add("peek");
        history.Add("cd src");
        Assert.Equal(["cd src", "peek"], history.All);

        var recall = new CommandRecall(history);
        Assert.Equal("cd src", recall.Previous(string.Empty));
        Assert.Equal("peek", recall.Previous("cd src"));
        Assert.Equal("cd src", recall.Next());
    }

    /// <summary>
    /// Every command of the table is handled in RunCommand and has a row in the help (as Blyant's CommandTests: the
    /// source is read)
    /// </summary>
    [Fact]
    public void Commands_AreHandledAndInTheHelp()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "BlyantValidator.slnx"))) root = root.Parent!;
        var source = File.ReadAllText(Path.Combine(root.FullName, "src", "Blyant.Validator.Cli", "Tui", "Browser.Commands.cs"));
        var help = string.Join(' ', Browser.HelpRows.Select(r => r.Key));

        var missing = new List<string>();
        foreach (var name in Browser.CommandTable.Names.Concat(Browser.CommandTable.ShortForms))
        {
            if (!source.Contains($"\"{name}\" or", StringComparison.Ordinal) &&
                !source.Contains($"or \"{name}\"", StringComparison.Ordinal) &&
                !source.Contains($"case \"{name}\":", StringComparison.Ordinal))
                missing.Add($"{name}: not in RunCommand");
        }

        foreach (var name in Browser.CommandTable.Names)
        {
            if (!help.Contains(":" + name, StringComparison.Ordinal)) missing.Add($"{name}: not in the help");
        }

        Assert.Empty(missing);
    }
}
