using System.Diagnostics;
using System.Text.RegularExpressions;
using Blyant.Validator.Core;
using Blyant.Validator.Typst;

namespace Blyant.Validator.TypstCompiler;

/// <summary>Where to find the Typst CLI.</summary>
/// <param name="TypstPath">Path to the typst executable. Falls back to the BLYANT_TYPST environment variable, then to
/// "typst" on PATH.</param>
public sealed record TypstCompilerOptions(string? TypstPath = null)
{
    public string ResolveTypst() =>
        TypstPath ?? (Environment.GetEnvironmentVariable("BLYANT_TYPST") is { Length: > 0 } fromEnvironment ? fromEnvironment : "typst");
}

public sealed class TypstNotAvailableException(string message, Exception? inner = null) : EngineNotAvailableException(message, inner);

/// <summary>
/// Optional engine for Typst files that runs the Typst CLI (https://github.com/typst/typst): <c>typst compile</c>,
/// with its PDF thrown away, and its errors and warnings shown. It finds all that the compiler finds: also what code
/// means (an unknown function, a wrong argument, a missing font), which the native checks leave out. Like the
/// compiler, it stops at the first error of the code, after the errors of the syntax.
///
/// A chapter is compiled as part of its document: when other files include it (looked for as the native checks do,
/// <see cref="TypstValidator.MainFile"/>), the main file is compiled, and the problems in the chapter are kept.
/// Typst downloads the packages a document imports (@preview/…) the first time: with this engine the program can go
/// online, which the native checks never do.
/// </summary>
public sealed partial class TypstCompilerValidator(TypstCompilerOptions options, TypstFiles? files = null) : IValidator
{
    /// <summary>How long the compiler may take for a document before it is stopped.</summary>
    public static TimeSpan Timeout { get; } = TimeSpan.FromMinutes(2);

    public string Engine => "typst";

    public bool CanValidate(DocumentKind kind) => kind == DocumentKind.Typst;

    public async Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default)
    {
        var file = Path.GetFullPath(document.Path);
        var main = files == null ? file : TypstValidator.MainFile(file, files);
        var psi = new ProcessStartInfo(options.ResolveTypst())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(main) ?? ".",
            StandardErrorEncoding = new System.Text.UTF8Encoding(false),
        };
        // The PDF goes to the standard output, and is thrown away: no file is written
        foreach (var argument in new[] { "compile", main, "-", "--format", "pdf", "--diagnostic-format", "short" })
            psi.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new TypstNotAvailableException("Could not start the Typst CLI.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new TypstNotAvailableException(
                $"Could not start '{psi.FileName}'. Install the Typst CLI (https://github.com/typst/typst) or pass --typst <path>.", ex);
        }

        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(Timeout);
            string errors;
            try
            {
                var pdf = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, timeout.Token);
                var output = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await pdf.ConfigureAwait(false);
                errors = await output.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stopped (the program closes) or too slow: the compiler must not go on running on its own
                Stop(process);
                if (cancellationToken.IsCancellationRequested) throw;
                throw new TypstNotAvailableException($"Typst did not finish within {Timeout.TotalMinutes:0} minutes, and was stopped.");
            }

            var found = Parse(errors, document.Path, file, main);
            // (an exit code of failure with nothing to tell: the compiler could not run)
            if (process.ExitCode != 0 && found.Count == 0 && !Diagnostics().IsMatch(errors))
                throw new TypstNotAvailableException($"typst failed (exit code {process.ExitCode}). {errors.Trim()}");
            return found;
        }
    }

    /// <summary>
    /// All the problems in the output of <c>typst compile --diagnostic-format short</c> of a document, in whichever of
    /// its files they are (each told with its full path); the problems of the document as a whole are told on the
    /// compiled file.
    /// </summary>
    public static IReadOnlyList<Diagnostic> ParseAll(string output, string compiled)
    {
        var files = new List<string> { compiled };
        foreach (var line in output.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (Located().Match(line) is not { Success: true } located) continue;
            var where = located.Groups["path"].Value;
            if (where.StartsWith(@"\\?\", StringComparison.Ordinal)) where = where[4..];
            try
            {
                where = Path.GetFullPath(where, Path.GetDirectoryName(compiled) ?? ".");
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!files.Contains(where, PathComparer)) files.Add(where);
        }

        return [.. files.SelectMany(file => Parse(output, file, file, compiled))];
    }

    private static StringComparer PathComparer => OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    internal static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // It ended meanwhile, or cannot be stopped
        }
    }

    /// <summary>
    /// The problems of a file in the output of <c>typst compile --diagnostic-format short</c> of a document: lines
    /// "path:line:column: error: message" (the column counted from 0) and "error: message" (of the document as a
    /// whole, told on the compiled file)
    /// </summary>
    /// <param name="path">The path of the file, as the diagnostics tell it.</param>
    /// <param name="file">Its full path.</param>
    /// <param name="compiled">The full path of the file that was compiled (the file, or the main file it is in).</param>
    public static IReadOnlyList<Diagnostic> Parse(string output, string path, string file, string compiled)
    {
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var result = new List<Diagnostic>();
        foreach (var line in output.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            SourcePosition at;
            string severity, message;
            if (Located().Match(line) is { Success: true } located)
            {
                // (Windows paths come as \\?\C:\...)
                var where = located.Groups["path"].Value;
                if (where.StartsWith(@"\\?\", StringComparison.Ordinal)) where = where[4..];
                if (!comparer.Equals(Path.GetFullPath(where), file)) continue;
                at = new SourcePosition(int.Parse(located.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(located.Groups["column"].Value, System.Globalization.CultureInfo.InvariantCulture) + 1);
                (severity, message) = (located.Groups["severity"].Value, located.Groups["message"].Value);
            }
            else if (Unlocated().Match(line) is { Success: true } unlocated && comparer.Equals(file, compiled))
            {
                at = new SourcePosition(1, 1);
                (severity, message) = (unlocated.Groups["severity"].Value, unlocated.Groups["message"].Value);
            }
            else
            {
                continue;
            }

            var isError = severity == "error";
            // (paths in a message also come as \\?\C:\...)
            message = message.Replace(@"\\?\", string.Empty, StringComparison.Ordinal).Trim();
            result.Add(new Diagnostic(path, isError ? Severity.Error : Severity.Warning,
                isError ? "typst.compiler-error" : "typst.compiler-warning", message, at, at, "typst"));
        }

        return result;
    }

    [GeneratedRegex(@"^(?<path>.+?):(?<line>[0-9]+):(?<column>[0-9]+): (?<severity>error|warning): (?<message>.*)$")]
    private static partial Regex Located();

    [GeneratedRegex(@"^(?<severity>error|warning): (?<message>.*)$")]
    private static partial Regex Unlocated();

    [GeneratedRegex(@"(^|\n)([^\n]*: )?(error|warning): ")]
    private static partial Regex Diagnostics();
}
