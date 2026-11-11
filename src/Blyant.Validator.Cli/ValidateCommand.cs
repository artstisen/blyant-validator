using System.Text.Json;
using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Html;
using Blyant.Validator.Vnu;
using Blyant.Validator.Markdown;
using Blyant.Validator.Typst;
using Blyant.Validator.Xml;

namespace Blyant.Validator.Cli;

internal static class ValidateCommand
{
    public sealed record Settings(
        string[] Paths, bool Recursive, string Engine, string Output, string? VnuJar, string? Java,
        bool Fragment = false, IReadOnlyList<string>? Ignore = null);

    public static async Task<int> RunAsync(Settings settings, CancellationToken ct)
    {
        var files = FileCollector.Collect(settings.Paths, settings.Recursive, Console.Error, out var missing);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("blyant-validator: no files to validate.");
            return 2;
        }

        IValidator[] validators = settings.Engine == "vnu"
            ? [new VnuValidator(new VnuOptions(settings.VnuJar, settings.Java)), new XmlValidator(), Markdown(), Typst()]
            : [new HtmlValidator(settings.Fragment), new CssValidator(), new XmlValidator(), Markdown(), Typst()];
        var ignore = settings.Ignore ?? [];
        if (settings.Fragment && settings.Engine == "vnu")
            Console.Error.WriteLine("blyant-validator: --fragment works with the native engine only; vnu checks HTML files as pages.");

        var all = new List<Diagnostic>();
        var checkedCount = 0;
        var ignoredCount = 0;
        var failed = missing > 0;
        foreach (var file in files)
        {
            SourceDocument document;
            try
            {
                document = await DocumentIo.LoadAsync(file, ct);
            }
            catch (NotUtf8Exception e)
            {
                // A problem of the file: reported with the others (and can be ignored by its code)
                var shown = IgnoreList.Apply([e.ToDiagnostic(file)], ignore);
                ignoredCount += 1 - shown.Count;
                all.AddRange(shown);
                checkedCount++;
                continue;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"blyant-validator: '{file}' cannot be read: {e.Message}");
                failed = true;
                continue;
            }

            var validator = validators.FirstOrDefault(v => v.CanValidate(document.Kind));
            if (validator is null)
            {
                Console.Error.WriteLine($"blyant-validator: skipping '{file}' (not an HTML, CSS, XML, Markdown or Typst file).");
                continue;
            }

            try
            {
                var found = await validator.ValidateAsync(document, ct);
                var shown = IgnoreList.Apply(found, ignore);
                ignoredCount += found.Count - shown.Count;
                all.AddRange(shown);
                checkedCount++;
            }
            catch (VnuNotAvailableException ex)
            {
                Console.Error.WriteLine($"blyant-validator: {ex.Message}");
                return 2;
            }
        }

        var mode = new Mode(settings.Fragment && settings.Engine != "vnu", ignore, ignoredCount);
        if (settings.Output == "json") WriteJson(all, mode, Console.Out);
        else WriteText(all, checkedCount, mode, Console.Out);

        // (a file that could not be read, or a path that does not exist: the tool did not do all it was asked)
        if (failed) return 2;
        return all.Any(d => d.Severity == Severity.Error) ? 1 : 0;
    }

    // Markdown, with relative links checked against the files on disk
    private static MarkdownValidator Markdown() => new(DocumentIo.IsFileOrFolder);

    // Typst, with the files around a file (included files, images, the files that include it)
    private static TypstValidator Typst() => new(DocumentIo.TypstFiles);

    /// <summary>How the files were checked, when it was not a full check: shown with the results.</summary>
    private sealed record Mode(bool Fragment, IReadOnlyList<string> Ignore, int IgnoredCount)
    {
        public string Note
        {
            get
            {
                var parts = new List<string>();
                if (Fragment) parts.Add("HTML checked as fragments");
                if (Ignore.Count > 0)
                    parts.Add($"{IgnoredCount} problem(s) ignored ({string.Join(", ", Ignore)})");
                return parts.Count == 0 ? string.Empty : " Not a full check: " + string.Join("; ", parts) + ".";
            }
        }
    }

    /// <summary>One line per finding, in the common <c>file:line:column: severity: message</c> form.</summary>
    private static void WriteText(List<Diagnostic> diagnostics, int fileCount, Mode mode, TextWriter output)
    {
        foreach (var d in diagnostics)
        {
            var severity = d.Severity.ToString().ToLowerInvariant();
            output.WriteLine($"{d.File}:{d.Start}: {severity}: {d.Message} [{d.Code}]");
        }

        var errors = diagnostics.Count(d => d.Severity == Severity.Error);
        var warnings = diagnostics.Count(d => d.Severity == Severity.Warning);
        output.WriteLine($"{fileCount} file(s) checked: {errors} error(s), {warnings} warning(s).{mode.Note}");
    }

    /// <summary>Machine-readable output for editors such as Blyant.</summary>
    private static void WriteJson(List<Diagnostic> diagnostics, Mode mode, TextWriter output)
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
            // How the files were checked (fields are only ever added, as Blyant reads this)
            json.WriteBoolean("fragment", mode.Fragment);
            json.WriteStartArray("ignore");
            foreach (var entry in mode.Ignore) json.WriteStringValue(entry);
            json.WriteEndArray();
            json.WriteNumber("ignored", mode.IgnoredCount);
            json.WriteEndObject();
        }
        output.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }
}
