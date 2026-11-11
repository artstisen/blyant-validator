using System.Diagnostics;
using Blyant.Validator.Cli;
using Blyant.Validator.Core;
using Blyant.Validator.Typst;
using Blyant.Validator.TypstCompiler;

namespace Blyant.Validator.Tests;

/// <summary>
/// The typst engine: reading the output of the Typst CLI, choosing the engines, and finding the main file. The tests
/// that run the compiler are skipped where it is not installed (as on the GitHub workflow).
/// </summary>
public sealed class TypstEngineTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blyant-validator-typst-" + Guid.NewGuid().ToString("N"));

    public TypstEngineTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void TheOutputOfTheCompiler_IsRead()
    {
        var main = Path.Combine(_dir, "main.typ");
        var chapter = Path.Combine(_dir, "ch", "one.typ");
        var output = string.Join('\n',
            $@"\\?\{chapter}:3:22: error: unexpected argument: bogus",
            $@"\\?\{main}:1:15: warning: unknown font family: nosuchfont",
            $@"\\?\{main}:2:7: error: file not found (searched at \\?\{Path.Combine(_dir, "x.png")})",
            "error: failed to load something of the document",
            "  hint: lines that are not diagnostics are passed over");

        var inChapter = TypstCompilerValidator.Parse(output, "ch/one.typ", chapter, main);
        var d = Assert.Single(inChapter);
        Assert.Equal(("typst.compiler-error", Severity.Error, new SourcePosition(3, 23), "typst"), (d.Code, d.Severity, d.Start, d.Engine));
        Assert.Equal("unexpected argument: bogus", d.Message);

        var inMain = TypstCompilerValidator.Parse(output, "main.typ", main, main);
        Assert.Equal(3, inMain.Count);
        Assert.Equal(Severity.Warning, inMain[0].Severity);
        Assert.DoesNotContain(@"\\?\", inMain[1].Message, StringComparison.Ordinal);
        Assert.Equal(new SourcePosition(1, 1), inMain[2].Start);
    }

    [Theory]
    [InlineData("native", false, false)]
    [InlineData("vnu", true, false)]
    [InlineData("typst", false, true)]
    [InlineData("vnu,typst", true, true)]
    [InlineData(" Typst , VNU ", true, true)]
    public void Engines_AreRead(string text, bool vnu, bool typst)
    {
        var engines = Engines.Parse(text);
        Assert.Equal(new Engines(vnu, typst), engines);
        Assert.Equal(engines, Engines.Parse(engines!.ToString()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("html")]
    [InlineData("native,vnu")]
    [InlineData("vnu,,java")]
    public void OtherEngines_AreRefused(string text)
    {
        Assert.Null(Engines.Parse(text));
    }

    [Fact]
    public void TheMainFile_IsTheFileThatIncludesTheChapters()
    {
        var main = Path.Combine(_dir, "main.typ");
        var part = Path.Combine(_dir, "parts", "part.typ");
        var chapter = Path.Combine(_dir, "parts", "chapter.typ");
        Directory.CreateDirectory(Path.Combine(_dir, "parts"));
        File.WriteAllText(main, "#include \"parts/part.typ\"\n");
        File.WriteAllText(part, "#include \"chapter.typ\"\n");
        File.WriteAllText(chapter, "= Chapter\n");

        Assert.Equal(main, TypstValidator.MainFile(chapter, DocumentIo.TypstFiles), ignoreCase: !OperatingSystem.IsLinux());
        Assert.Equal(main, TypstValidator.MainFile(main, DocumentIo.TypstFiles), ignoreCase: !OperatingSystem.IsLinux());
    }

    [Fact]
    public async Task ACompilerThatCannotBeStarted_IsTold()
    {
        var file = Path.Combine(_dir, "a.typ");
        File.WriteAllText(file, "= A\n");
        var engine = new TypstCompilerValidator(new TypstCompilerOptions(Path.Combine(_dir, "no-typst")));
        await Assert.ThrowsAsync<TypstNotAvailableException>(() =>
            engine.ValidateAsync(new SourceDocument(file, "= A\n", DocumentKind.Typst)));
    }

    [TypstFact]
    public async Task TheCompiler_FindsWhatCodeMeans_InAChapterOfItsDocument()
    {
        var main = Path.Combine(_dir, "main.typ");
        var chapter = Path.Combine(_dir, "ch", "one.typ");
        Directory.CreateDirectory(Path.Combine(_dir, "ch"));
        File.WriteAllText(main, "= Main <main>\n#include \"ch/one.typ\"\n");
        File.WriteAllText(chapter, "== One\nSee @main.\n#text(size: \"big\")[x]\n");

        var engine = new TypstCompilerValidator(new TypstCompilerOptions(), DocumentIo.TypstFiles);
        var found = await engine.ValidateAsync(DocumentIo.Load(chapter));
        var d = Assert.Single(found);
        Assert.Equal(("typst.compiler-error", 3), (d.Code, d.Start.Line));
        Assert.Empty(await engine.ValidateAsync(DocumentIo.Load(main)));
    }
}

/// <summary>A test that runs the Typst CLI: where it is not installed it is shown as skipped (not as passed)</summary>
public sealed class TypstFactAttribute : FactAttribute
{
    private static readonly bool HasTypst = CanRun();

    public TypstFactAttribute()
    {
        if (!HasTypst) Skip = "The Typst CLI (typst) is not installed";
    }

    private static bool CanRun()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("typst", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process == null) return false;
            process.WaitForExit(10_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
