using System.Diagnostics;
using System.Text.Json;
using Blyant.Validator.Core;

namespace Blyant.Validator.Vnu;

/// <summary>Where to find Java and vnu.jar.</summary>
/// <param name="JarPath">Path to vnu.jar. Falls back to the BLYANT_VNU_JAR environment variable.</param>
/// <param name="JavaPath">Path to the java executable. Falls back to JAVA_HOME, then to "java" on PATH.</param>
public sealed record VnuOptions(string? JarPath = null, string? JavaPath = null)
{
    public string ResolveJar() =>
        JarPath
        ?? Environment.GetEnvironmentVariable("BLYANT_VNU_JAR")
        ?? throw new VnuNotAvailableException(
            "vnu.jar not found. Pass --vnu-jar <path> or set the BLYANT_VNU_JAR environment variable. " +
            "Download it from https://github.com/validator/validator/releases");

    public string ResolveJava()
    {
        if (JavaPath is not null) return JavaPath;
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(javaHome))
        {
            var exe = OperatingSystem.IsWindows() ? "java.exe" : "java";
            var candidate = Path.Combine(javaHome, "bin", exe);
            if (File.Exists(candidate)) return candidate;
        }
        return "java";
    }
}

public sealed class VnuNotAvailableException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Optional backend that delegates to the Nu Html Checker (https://github.com/validator/validator)
/// by running <c>java -jar vnu.jar --format json</c>. Requires a Java runtime.
/// </summary>
public sealed class VnuValidator(VnuOptions options) : IValidator
{
    public string Engine => "vnu";

    public bool CanValidate(DocumentKind kind) => kind is DocumentKind.Html or DocumentKind.Css;

    public async Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default)
    {
        var jar = options.ResolveJar();
        if (!File.Exists(jar))
            throw new VnuNotAvailableException($"vnu.jar not found at '{jar}'.");

        var psi = new ProcessStartInfo(options.ResolveJava())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(jar);
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add("--stdout");
        if (document.Kind == DocumentKind.Css) psi.ArgumentList.Add("--css");
        psi.ArgumentList.Add(Path.GetFullPath(document.Path));

        Process process;
        try
        {
            process = Process.Start(psi) ?? throw new VnuNotAvailableException("Could not start Java.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new VnuNotAvailableException(
                $"Could not start '{psi.FileName}'. Install a Java runtime or pass --java <path>.", ex);
        }

        using (process)
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(stdout))
                throw new VnuNotAvailableException($"vnu produced no output (exit code {process.ExitCode}). {stderr.Trim()}");

            return Parse(document.Path, stdout);
        }
    }

    /// <summary>Converts vnu's JSON output (<c>{"messages":[...]}</c>) into diagnostics.</summary>
    public static IReadOnlyList<Diagnostic> Parse(string file, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<Diagnostic>();
        if (!doc.RootElement.TryGetProperty("messages", out var messages)) return result;

        foreach (var m in messages.EnumerateArray())
        {
            var type = GetString(m, "type");
            var subType = GetString(m, "subType");
            var severity = (type, subType) switch
            {
                ("error", _) or ("non-document-error", _) => Severity.Error,
                ("info", "warning") => Severity.Warning,
                _ => Severity.Info,
            };

            var last = new SourcePosition(GetInt(m, "lastLine"), GetInt(m, "lastColumn"));
            var firstLine = GetInt(m, "firstLine");
            var first = new SourcePosition(firstLine == 0 ? last.Line : firstLine, GetInt(m, "firstColumn"));

            var code = subType is null ? $"vnu.{type}" : $"vnu.{type}.{subType}";
            result.Add(new Diagnostic(file, severity, code, GetString(m, "message") ?? "", first, last, "vnu"));
        }
        return result;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
}
