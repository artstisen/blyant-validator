using Blyant.Validator.Cli.Tui.Terminal;
using Blyant.Validator.Core;
using Blyant.Validator.Typst;
using Blyant.Validator.TypstCompiler;

namespace Blyant.Validator.Cli.Tui;

// ":typst": make a PDF, PNG or SVG pages, or HTML of the Typst document of the selected file, with the Typst CLI
internal static partial class Browser
{
    // Where the Typst CLI is (--typst, BLYANT_TYPST or PATH), as for :engine typst
    private static TypstCompilerOptions _typst = new();

    private static readonly (string Name, string Key, TypstFormat Format)[] TypstFormats =
    [
        ("PDF", "P", TypstFormat.Pdf),
        ("PNG, one image per page", "N", TypstFormat.Png),
        ("SVG, one image per page", "S", TypstFormat.Svg),
        ("HTML (experimental in Typst)", "H", TypstFormat.Html),
    ];

    // The document of the selected Typst file is made by Typst in a hidden folder next to its main file; only when it
    // has no errors are the files put in place, next to the main file, after asking when they replace files
    private static void ExportTypst()
    {
        if (SelectedItem() is not { IsDirectory: false } item || SourceDocument.DetectKind(item.Path) != DocumentKind.Typst)
        {
            _message = "Select a Typst file";
            return;
        }

        var main = TypstValidator.MainFile(Path.GetFullPath(item.Path), DocumentIo.TypstFiles);
        var mainName = Path.GetFileName(main);
        var question = $"Make {mainName} into:";
        var note = SamePath(item.Path, main) ? "With Typst, next to it." : $"The document {Path.GetFileName(item.Path)} is part of, with Typst.";
        var picked = ChoiceBox.Pick("Typst", question, note, [.. TypstFormats.Select(f => f.Name)],
            [.. TypstFormats.Select(f => f.Key)], 0);
        if (picked < 0) return;
        var format = TypstFormats[picked].Format;
        // (the box is gone before Typst runs)
        Draw(true);

        Tools.WriteBottomLine($" Typst is making {TypstExporter.OutputName(main, format).Replace("{p}", "*", StringComparison.Ordinal)} (Esc: stop) ",
            ConsoleColors.ColorPreset.Normal, false);
        PreparedExport export;
        using (var stop = new CancellationTokenSource())
        {
            var task = TypstExporter.PrepareAsync(_typst, main, format, stop.Token);
            try
            {
                while (!task.Wait(100))
                {
                    while (Console.KeyAvailable)
                    {
                        if (Console.ReadKey(true).Key == ConsoleKey.Escape) stop.Cancel();
                    }
                }

                export = task.Result;
            }
            catch (AggregateException e) when (e.InnerException is OperationCanceledException)
            {
                _message = "Stopped: nothing was made";
                return;
            }
            catch (AggregateException e) when (e.InnerException is EngineNotAvailableException or IOException or UnauthorizedAccessException)
            {
                _message = "Not made: " + e.InnerException.Message;
                return;
            }
        }

        using (export)
        {
            if (!export.Succeeded)
            {
                ShowTypstProblems(export, main);
                _message = "Not made: the document has errors";
                return;
            }

            var existing = export.Existing;
            if (existing.Count > 0)
            {
                var replace = existing.Count == 1 ? $"Replace {Path.GetFileName(existing[0])}?" : $"Replace {existing.Count} files?";
                if (!ChoiceBox.Confirm("Typst", replace, $"Typst made {Made(export)}.")) return;
            }

            IReadOnlyList<string> written;
            try
            {
                written = export.Commit();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _message = "Not put in place: " + e.Message;
                return;
            }

            foreach (var pane in Panes.Where(p => p.Directory.Length > 0 && SamePath(p.Directory, Path.GetDirectoryName(main) ?? string.Empty))) Reload(pane);
            // (made HTML is checked, as any other)
            foreach (var path in written.Where(p => ResultStore.IsValidated(p))) _results.Refresh(path);
            var warnings = export.Problems.Count;
            _message = $"Made {Made(export)}" + (warnings > 0 ? $" ({warnings} warning{(warnings == 1 ? "" : "s")} of Typst)" : "");
        }
    }

    private static string Made(PreparedExport export) =>
        export.Files.Count == 1 ? Path.GetFileName(export.Files[0].Target) : $"{export.Files.Count} files";

    private static void ShowTypstProblems(PreparedExport export, string main)
    {
        var folder = Path.GetDirectoryName(main) ?? string.Empty;
        var texts = new List<(string, ConsoleColors.ColorPreset)>
        {
            ($"{Path.GetFileName(main)} was not made into a file: nothing was written.", ConsoleColors.ColorPreset.Normal),
            (string.Empty, ConsoleColors.ColorPreset.Normal),
        };
        foreach (var problem in export.Problems.OrderBy(p => p.Severity == Severity.Error ? 0 : 1))
        {
            var where = Path.GetRelativePath(folder, problem.File);
            texts.Add(($"{where}:{problem.Start.Line}:{problem.Start.Column}: {(problem.Severity == Severity.Error ? "error" : "warning")}: {problem.Message}",
                problem.Severity == Severity.Error ? ConsoleColors.ColorPreset.Normal : ConsoleColors.ColorPreset.Divider));
        }

        ChoiceBox.ShowTexts("Typst", texts);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
