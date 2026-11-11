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

    [Theory]
    [InlineData(TypstFormat.Pdf, "book.pdf")]
    [InlineData(TypstFormat.Png, "book-{p}.png")]
    [InlineData(TypstFormat.Svg, "book-{p}.svg")]
    [InlineData(TypstFormat.Html, "book.html")]
    public void TheOutput_IsNamedAfterTheMainFile(TypstFormat format, string name)
    {
        Assert.Equal(name, TypstExporter.OutputName(Path.Combine(_dir, "book.typ"), format));
    }

    [Fact]
    public async Task AnExportThatCannotBeStarted_LeavesNothing()
    {
        var main = Path.Combine(_dir, "main.typ");
        File.WriteAllText(main, "= A\n");
        await Assert.ThrowsAsync<TypstNotAvailableException>(() =>
            TypstExporter.PrepareAsync(new TypstCompilerOptions(Path.Combine(_dir, "no-typst")), main, TypstFormat.Pdf));
        Assert.Equal([main], Directory.GetFileSystemEntries(_dir));
    }

    [TypstFact]
    public async Task AnExport_IsOnlyPutInPlaceWhenCommitted()
    {
        var main = Path.Combine(_dir, "main.typ");
        var pdf = Path.Combine(_dir, "main.pdf");
        Directory.CreateDirectory(Path.Combine(_dir, "ch"));
        File.WriteAllText(main, "= Main\n#include \"ch/one.typ\"\n");
        File.WriteAllText(Path.Combine(_dir, "ch", "one.typ"), "== One\n");
        File.WriteAllText(pdf, "old");

        using (var export = await TypstExporter.PrepareAsync(new TypstCompilerOptions(), main, TypstFormat.Pdf))
        {
            Assert.True(export.Succeeded);
            Assert.Equal("old", File.ReadAllText(pdf));
            Assert.Equal(pdf, Assert.Single(export.Existing), ignoreCase: !OperatingSystem.IsLinux());
            export.Commit();
        }

        Assert.StartsWith("%PDF", File.ReadAllText(pdf), StringComparison.Ordinal);
        Assert.Equal(3, Directory.GetFileSystemEntries(_dir).Length);
    }

    [TypstFact]
    public async Task ADocumentWithErrors_IsNotMade_AndItsErrorsAreTold()
    {
        var main = Path.Combine(_dir, "main.typ");
        var chapter = Path.Combine(_dir, "ch", "one.typ");
        var pdf = Path.Combine(_dir, "main.pdf");
        Directory.CreateDirectory(Path.Combine(_dir, "ch"));
        File.WriteAllText(main, "= Main\n#include \"ch/one.typ\"\n");
        File.WriteAllText(chapter, "== One\n#text(size: \"big\")[x]\n");
        File.WriteAllText(pdf, "old");

        using (var export = await TypstExporter.PrepareAsync(new TypstCompilerOptions(), main, TypstFormat.Pdf))
        {
            Assert.False(export.Succeeded);
            Assert.Empty(export.Files);
            var d = Assert.Single(export.Problems);
            Assert.Equal(chapter, d.File, ignoreCase: !OperatingSystem.IsLinux());
            Assert.Equal(("typst.compiler-error", 2), (d.Code, d.Start.Line));
            Assert.Throws<InvalidOperationException>(() => export.Commit());
        }

        Assert.Equal("old", File.ReadAllText(pdf));
        Assert.Equal(3, Directory.GetFileSystemEntries(_dir).Length);
    }

    [TypstFact]
    public async Task Images_AreMadeOnePerPage_AndHtmlWithItsFeature()
    {
        var main = Path.Combine(_dir, "main.typ");
        File.WriteAllText(main, "= One\n#pagebreak()\n= Two\n");
        var typst = new TypstCompilerOptions();

        using (var png = await TypstExporter.PrepareAsync(typst, main, TypstFormat.Png))
            Assert.Equal(["main-1.png", "main-2.png"], png.Files.Select(f => Path.GetFileName(f.Target)));
        using (var svg = await TypstExporter.PrepareAsync(typst, main, TypstFormat.Svg))
            Assert.Equal(["main-1.svg", "main-2.svg"], svg.Files.Select(f => Path.GetFileName(f.Target)));
        using (var html = await TypstExporter.PrepareAsync(typst, main, TypstFormat.Html))
        {
            Assert.True(html.Succeeded);
            Assert.Equal("main.html", Path.GetFileName(Assert.Single(html.Files).Target));
        }

        // (nothing committed: nothing left)
        Assert.Equal([main], Directory.GetFileSystemEntries(_dir));
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
