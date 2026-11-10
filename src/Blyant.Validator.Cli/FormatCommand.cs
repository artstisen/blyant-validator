using Blyant.Validator.Core;
using Blyant.Validator.Css;
using Blyant.Validator.Html;

namespace Blyant.Validator.Cli;

internal static class FormatCommand
{
    public sealed record Settings(
        string[] Paths, bool Recursive, bool Write, bool Check, int IndentSize, bool UseTabs);

    public static async Task<int> RunAsync(Settings settings, CancellationToken ct)
    {
        var files = FileCollector.Collect(settings.Paths, settings.Recursive, Console.Error);
        if (files.Count == 0)
        {
            Console.Error.WriteLine("blyant-validator: no files to format.");
            return 2;
        }

        var options = new FormatOptions { IndentSize = settings.IndentSize, UseTabs = settings.UseTabs };
        IFormatter[] formatters = [new HtmlFormatter(), new CssFormatter()];
        var unformatted = 0;

        foreach (var file in files)
        {
            var document = await SourceDocument.LoadAsync(file, ct);
            var formatter = formatters.FirstOrDefault(f => f.CanFormat(document.Kind));
            if (formatter is null)
            {
                Console.Error.WriteLine($"blyant-validator: skipping '{file}' (not an HTML or CSS file).");
                continue;
            }

            string formatted;
            try
            {
                formatted = formatter.Format(document, options);
            }
            catch (NotImplementedException ex)
            {
                Console.Error.WriteLine($"blyant-validator: {ex.Message}");
                return 2;
            }

            if (settings.Check)
            {
                if (formatted != document.Content)
                {
                    Console.Out.WriteLine($"{file}: not formatted");
                    unformatted++;
                }
            }
            else if (settings.Write)
            {
                if (formatted != document.Content)
                    await File.WriteAllTextAsync(file, formatted, ct);
            }
            else
            {
                Console.Out.Write(formatted);
            }
        }

        return unformatted > 0 ? 1 : 0;
    }
}
