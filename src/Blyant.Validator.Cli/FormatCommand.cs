using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Html;
using Blyant.Validator.Xml;

namespace Blyant.Validator.Cli;

internal static class FormatCommand
{
    public sealed record Settings(
        string[] Paths, bool Recursive, bool Write, bool Check, int IndentSize, bool UseTabs, int LineWidth);

    public static async Task<int> RunAsync(Settings settings, CancellationToken ct)
    {
        var files = FileCollector.Collect(settings.Paths, settings.Recursive, Console.Error, out var missing);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("blyant-validator: no files to format.");
            return 2;
        }

        var options = new FormatOptions
        {
            IndentSize = settings.IndentSize,
            UseTabs = settings.UseTabs,
            LineWidth = settings.LineWidth,
        };
        IFormatter[] formatters = [new HtmlFormatter(), new CssFormatter(), new XmlFormatter()];
        var unformatted = 0;
        var failed = missing;

        foreach (var file in files)
        {
            SourceDocument document;
            try
            {
                document = await DocumentIo.LoadAsync(file, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // (a file that is not UTF-8 is not formatted: its bytes would be lost)
                Console.Error.WriteLine($"blyant-validator: '{file}' was not formatted: {e.Message}");
                failed++;
                continue;
            }
            var formatter = formatters.FirstOrDefault(f => f.CanFormat(document.Kind));
            if (formatter is null)
            {
                Console.Error.WriteLine($"blyant-validator: skipping '{file}' (only HTML, CSS and XML files are formatted).");
                continue;
            }

            string formatted;
            try
            {
                formatted = formatter.Format(document, options);
            }
            catch (Core.FormatException ex)
            {
                Console.Error.WriteLine($"blyant-validator: '{file}' was not formatted: {ex.Message}");
                failed++;
                continue;
            }

            var changed = formatted != document.Content;
            if (settings.Check)
            {
                if (changed)
                {
                    Console.Out.WriteLine($"{file}: not formatted");
                    unformatted++;
                }
            }
            else if (settings.Write)
            {
                if (changed)
                {
                    try
                    {
                        DocumentIo.SafeWriteAllText(file, formatted, document.HasUtf8Bom);
                        Console.Out.WriteLine($"{file}: formatted");
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        Console.Error.WriteLine($"blyant-validator: '{file}' could not be written: {e.Message}");
                        failed++;
                    }
                }
            }
            else
            {
                Console.Out.Write(formatted);
            }
        }

        if (failed > 0) return 2;
        return unformatted > 0 ? 1 : 0;
    }
}
