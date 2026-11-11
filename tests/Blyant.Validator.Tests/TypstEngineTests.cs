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

    [Fact]
    public void PathsFromTheWorkingDirectory_AreRead_AsTypstWritesThemOnLinuxAndMacOS()
    {
        // (Typst runs in the folder of the main file, and tells the files of the document from there)
        var main = Path.Combine(_dir, "main.typ");
        var chapter = Path.Combine(_dir, "ch", "one.typ");
        var output = "ch/one.typ:3:22: error: unexpected argument: bogus\nmain.typ:1:15: warning: unknown font family: x\n";

        var d = Assert.Single(TypstCompilerValidator.Parse(output, "ch/one.typ", chapter, main));
        Assert.Equal(new SourcePosition(3, 23), d.Start);
        var all = TypstCompilerValidator.ParseAll(output, main);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, p => p.File == chapter && p.Severity == Severity.Error);
        Assert.Contains(all, p => p.File == main && p.Severity == Severity.Warning);
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

    // An export made by hand (as Typst makes it, in a hidden folder next to the main file), for the tests of putting it
    // in place that do not need Typst
    private PreparedExport Made(params (string Name, string Text)[] files)
    {
        var folder = Path.Combine(_dir, ".bv-typst-test");
        Directory.CreateDirectory(folder);
        foreach (var (name, text) in files) File.WriteAllText(Path.Combine(folder, name), text);
        return new PreparedExport(folder, Path.Combine(_dir, "main.typ"), [], [.. files.Select(f => Path.Combine(folder, f.Name))]);
    }

    [Fact]
    public void FilesThatCannotBePutInPlace_AreTold_WithThoseThatWere()
    {
        // (main-2.png is a folder: it cannot be replaced)
        Directory.CreateDirectory(Path.Combine(_dir, "main-2.png"));
        using var export = Made(("main-1.png", "one"), ("main-2.png", "two"));
        Assert.ThrowsAny<Exception>(() => export.Commit());
        Assert.Equal([Path.Combine(_dir, "main-1.png")], export.Placed);
        Assert.Equal("one", File.ReadAllText(Path.Combine(_dir, "main-1.png")));
    }

    [LinksFact]
    public void ALink_IsFollowed_ToTheFileItLeadsTo()
    {
        var output = Path.Combine(_dir, "out");
        Directory.CreateDirectory(output);
        var real = Path.Combine(output, "book.pdf");
        File.WriteAllText(real, "old");
        var link = Path.Combine(_dir, "main.pdf");
        File.CreateSymbolicLink(link, real);

        using (var export = Made(("main.pdf", "new")))
            export.Commit();

        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.Equal("new", File.ReadAllText(real));
        Assert.Empty(Directory.GetFiles(output, ".bv-*"));
    }

    [UnixFact]
    public void AReplacedFile_KeepsItsPermissions()
    {
        var pdf = Path.Combine(_dir, "main.pdf");
        File.WriteAllText(pdf, "old");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(pdf, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        using (var export = Made(("main.pdf", "new")))
            export.Commit();

        Assert.Equal("new", File.ReadAllText(pdf));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(pdf));
    }

    [WindowsFact]
    public void AReplacedFile_KeepsItsAttributes()
    {
        var pdf = Path.Combine(_dir, "main.pdf");
        File.WriteAllText(pdf, "old");
        File.SetAttributes(pdf, File.GetAttributes(pdf) | FileAttributes.Hidden);

        using (var export = Made(("main.pdf", "new")))
            export.Commit();

        Assert.Equal("new", File.ReadAllText(pdf));
        Assert.True(File.GetAttributes(pdf).HasFlag(FileAttributes.Hidden));
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

        using (var renamed = await TypstExporter.PrepareAsync(typst, main, TypstFormat.Png))
        {
            renamed.Rename("print");
            Assert.Empty(renamed.Existing);
            Assert.Equal(["print-1.png", "print-2.png"], renamed.Commit().Select(Path.GetFileName));
        }

        File.Delete(Path.Combine(_dir, "print-1.png"));
        File.Delete(Path.Combine(_dir, "print-2.png"));
        // (nothing else committed: nothing left)
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
