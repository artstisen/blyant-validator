using System.Diagnostics;
using Blyant.Validator.Core;

namespace Blyant.Validator.TypstCompiler;

/// <summary>What Typst can make of a document.</summary>
public enum TypstFormat
{
    Pdf,

    /// <summary>One PNG image per page.</summary>
    Png,

    /// <summary>One SVG image per page.</summary>
    Svg,

    /// <summary>HTML, which Typst only makes with its features turned on (it is still being made).</summary>
    Html,
}

/// <summary>
/// The output of a document, made by Typst in a hidden folder next to the main file, and not yet put in place: <see
/// cref="Commit"/> moves the files there (each in one step, replacing a file of the same name), <see cref="Dispose"/>
/// removes what was not moved. So a failed or stopped export never leaves a file half written, or changes the files
/// that were there.
/// </summary>
public sealed class PreparedExport : IDisposable
{
    private readonly string _folder;

    internal PreparedExport(string folder, string mainFile, IReadOnlyList<Diagnostic> problems, IReadOnlyList<string> made)
    {
        _folder = folder;
        MainFile = mainFile;
        Problems = problems;
        var target = Path.GetDirectoryName(mainFile) ?? ".";
        Files = [.. made.Order(StringComparer.Ordinal).Select(file => (file, Path.Combine(target, Path.GetFileName(file))))];
    }

    public string MainFile { get; }

    /// <summary>The errors and warnings of the document (of all its files).</summary>
    public IReadOnlyList<Diagnostic> Problems { get; }

    /// <summary>True when the document was made: no errors, and files to put in place.</summary>
    public bool Succeeded => Files.Count > 0 && !Problems.Any(p => p.Severity == Severity.Error);

    /// <summary>The files made (in the hidden folder), and where each goes.</summary>
    public IReadOnlyList<(string Made, string Target)> Files { get; }

    /// <summary>The files that would be replaced.</summary>
    public IReadOnlyList<string> Existing => [.. Files.Select(f => f.Target).Where(File.Exists)];

    /// <summary>Put the files in place, each in one step; returns where they went.</summary>
    public IReadOnlyList<string> Commit()
    {
        if (!Succeeded) throw new InvalidOperationException("The document was not made.");
        foreach (var (made, target) in Files) File.Move(made, target, overwrite: true);
        return [.. Files.Select(f => f.Target)];
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left: a hidden folder ".bv-typst-…" that can be deleted
        }
    }
}

/// <summary>
/// Makes the output of a Typst document with the Typst CLI (<c>typst compile</c>): PDF, PNG or SVG pages, or HTML. It
/// is written in a hidden folder next to the main file first (on the same disk, so it is moved into place in one
/// step), and only put in place when the document has no errors: see <see cref="PreparedExport"/>.
/// </summary>
public static class TypstExporter
{
    /// <summary>The file extension of a format.</summary>
    public static string Extension(TypstFormat format) => format switch
    {
        TypstFormat.Png => "png",
        TypstFormat.Svg => "svg",
        TypstFormat.Html => "html",
        _ => "pdf",
    };

    /// <summary>The name the output gets: "main.pdf", or for images one per page, "main-1.png" ….</summary>
    public static string OutputName(string mainFile, TypstFormat format)
    {
        var name = Path.GetFileNameWithoutExtension(mainFile);
        return format is TypstFormat.Png or TypstFormat.Svg ? $"{name}-{{p}}.{Extension(format)}" : $"{name}.{Extension(format)}";
    }

    /// <summary>Run Typst on the main file of a document, into a hidden folder next to it.</summary>
    public static async Task<PreparedExport> PrepareAsync(TypstCompilerOptions options, string mainFile, TypstFormat format,
        CancellationToken cancellationToken = default)
    {
        var main = Path.GetFullPath(mainFile);
        var folder = Path.Combine(Path.GetDirectoryName(main) ?? ".", $".bv-typst-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        if (OperatingSystem.IsWindows()) File.SetAttributes(folder, File.GetAttributes(folder) | FileAttributes.Hidden);

        var psi = new ProcessStartInfo(options.ResolveTypst())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(main) ?? ".",
            StandardErrorEncoding = new System.Text.UTF8Encoding(false),
            StandardOutputEncoding = new System.Text.UTF8Encoding(false),
        };
        foreach (var argument in new[] { "compile", main, Path.Combine(folder, OutputName(main, format)), "--diagnostic-format", "short" })
            psi.ArgumentList.Add(argument);
        if (format == TypstFormat.Html)
        {
            psi.ArgumentList.Add("--features");
            psi.ArgumentList.Add("html");
        }

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new TypstNotAvailableException("Could not start the Typst CLI.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Delete(folder);
            throw new TypstNotAvailableException(
                $"Could not start '{psi.FileName}'. Install the Typst CLI (https://github.com/typst/typst) or give its path.", ex);
        }

        string output;
        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(TypstCompilerValidator.Timeout);
            try
            {
                var errors = process.StandardError.ReadToEndAsync(timeout.Token);
                var rest = process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                output = await errors.ConfigureAwait(false) + "\n" + await rest.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                TypstCompilerValidator.Stop(process);
                Delete(folder);
                if (cancellationToken.IsCancellationRequested) throw;
                throw new TypstNotAvailableException($"Typst did not finish within {TypstCompilerValidator.Timeout.TotalMinutes:0} minutes, and was stopped.");
            }

            var problems = TypstCompilerValidator.ParseAll(output, main);
            var made = Directory.GetFiles(folder);
            if (process.ExitCode != 0 && !problems.Any(p => p.Severity == Severity.Error))
            {
                // (a failure with nothing to tell: told as an error of the main file)
                var at = new SourcePosition(1, 1);
                problems = [.. problems, new Diagnostic(main, Severity.Error, "typst.compiler-error",
                    $"typst failed (exit code {process.ExitCode}). {output.Trim()}", at, at, "typst")];
            }

            return new PreparedExport(folder, main, problems, process.ExitCode == 0 ? made : []);
        }
    }

    private static void Delete(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left: a hidden folder that can be deleted
        }
    }
}
