using System.Text.Json;
using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Html;
using Blyant.Validator.Vnu;

namespace Blyant.Validator.Cli;

internal static class ValidateCommand
{
    public sealed record Settings(
        string[] Paths, bool Recursive, string Engine, string Output, string? VnuJar, string? Java);

    public static async Task<int> RunAsync(Settings settings, CancellationToken ct)
    {
        var files = FileCollector.Collect(settings.Paths, settings.Recursive, Console.Error);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("blyant-validator: no files to validate.");
            return 2;
        }

        IValidator[] validators = settings.Engine == "vnu"
            ? [new VnuValidator(new VnuOptions(settings.VnuJar, settings.Java))]
            : [new HtmlValidator(), new CssValidator()];

        var all = new List<Diagnostic>();
        var checkedCount = 0;
        foreach (var file in files)
        {
            var document = await SourceDocument.LoadAsync(file, ct);
            var validator = validators.FirstOrDefault(v => v.CanValidate(document.Kind));
            if (validator is null)
            {
                Console.Error.WriteLine($"blyant-validator: skipping '{file}' (not an HTML or CSS file).");
                continue;
            }

            try
            {
                all.AddRange(await validator.ValidateAsync(document, ct));
                checkedCount++;
            }
            catch (NotImplementedException ex)
            {
                Console.Error.WriteLine($"blyant-validator: skipping '{file}': {ex.Message}");
            }
            catch (VnuNotAvailableException ex)
            {
                Console.Error.WriteLine($"blyant-validator: {ex.Message}");
                return 2;
            }
        }

        if (settings.Output == "json") WriteJson(all, Console.Out);
        else WriteText(all, checkedCount, Console.Out);

        return all.Any(d => d.Severity == Severity.Error) ? 1 : 0;
    }

    /// <summary>One line per finding, in the common <c>file:line:column: severity: message</c> form.</summary>
    private static void WriteText(List<Diagnostic> diagnostics, int fileCount, TextWriter output)
    {
        foreach (var d in diagnostics)
        {
            var severity = d.Severity.ToString().ToLowerInvariant();
            output.WriteLine($"{d.File}:{d.Start}: {severity}: {d.Message} [{d.Code}]");
        }

        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = diagnostics.Count(d => d.Severity == Severity.Warning);
        output.WriteLine($"{fileCount} file(s) checked: {errors} error(s), {warnings} warning(s).");
    }

    /// <summary>Machine-readable output for editors such as Blyant.</summary>
    private static void WriteJson(List<Diagnostic> diagnostics, TextWriter output)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteStartArray("diagnostics");
            foreach (var d in diagnostics)
            {
                json.WriteStartObject();
                json.WriteString("file", d.File);
                json.WriteString("severity", d.Severity.ToString().ToLowerInvariant());
                json.WriteString("code", d.Code);
                json.WriteString("message", d.Message);
                json.WriteNumber("line", d.Start.Line);
                json.WriteNumber("column", d.Start.Column);
                json.WriteNumber("endLine", d.End.Line);
                json.WriteNumber("endColumn", d.End.Column);
                json.WriteString("engine", d.Engine);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        output.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
