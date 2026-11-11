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
            if (!export.Succeeded && !export.Problems.Any(p => p.Severity == Severity.Error))
            {
                // (Typst ended well, but made no file)
                _message = "Not made: Typst made no file";
                return;
            }

            if (!export.Succeeded)
            {
                ShowTypstProblems(export, main);
                _message = "Not made: the document has errors";
                return;
            }

            if (!ChooseNames(export, format)) return;

            string? failed = null;
            try
            {
                export.Commit();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // (a file open in another program, as a PDF in a viewer on Windows: the files before it are in place)
                failed = e.Message;
            }

            var written = export.Placed;
            foreach (var pane in Panes.Where(p => p.Directory.Length > 0 && SamePath(p.Directory, Path.GetDirectoryName(main) ?? string.Empty))) Reload(pane);
            // (made HTML is checked, as any other)
            foreach (var path in written.Where(p => ResultStore.IsValidated(p))) _results.Refresh(path);
            var warnings = export.Problems.Count;
            _message = failed == null
                ? $"Made {Made(export)}" + (warnings > 0 ? $" ({warnings} warning{(warnings == 1 ? "" : "s")} of Typst)" : "")
                : written.Count == 0
                    ? "Not put in place: " + failed
                    : $"Put in place: {written.Count} of {export.Files.Count} files. Not {Path.GetFileName(export.Files[written.Count].Target)}: {failed}";
        }
    }

    // When files of the names are there: replace them, or save as another name (asked, next to the main file, until no
    // file has the name, or Replace is chosen). False when nothing is to be put in place
    private static bool ChooseNames(PreparedExport export, TypstFormat format)
    {
        var extension = "." + TypstExporter.Extension(format);
        var problem = string.Empty;
        while (export.Existing is { Count: > 0 } existing)
        {
            Draw(true);
            ChoiceBox.Forget();
            // (a folder of the name cannot be replaced)
            var folder = existing.FirstOrDefault(Directory.Exists);
            var question = folder != null ? $"There is a folder {Path.GetFileName(folder)}"
                : existing.Count == 1 ? $"Replace {Path.GetFileName(existing[0])}?" : $"Replace {existing.Count} files?";
            var note = problem.Length > 0 ? problem
                : existing.Count == 1 ? $"A file of that name is there. Typst made {Made(export)}."
                : $"Files of those names are there ({Path.GetFileName(existing[0])} …). Typst made {Made(export)}.";
            string[] choices = folder != null ? ["Save as another name", "Cancel"] : ["Replace", "Save as another name", "Cancel"];
            string[] keys = folder != null ? ["S", "C"] : ["R", "S", "C"];
            var picked = ChoiceBox.Pick("Typst", question, note, choices, keys, 0);
            if (picked < 0 || choices[picked] == "Cancel") return false;
            if (choices[picked] == "Replace") return true;

            Draw(true);
            ChoiceBox.Forget();
            var pages = format is TypstFormat.Png or TypstFormat.Svg;
            var stem = Path.GetFileNameWithoutExtension(export.Files[0].Target);
            if (pages) stem = stem[..stem.LastIndexOf('-')];
            var name = ChoiceBox.AskText("Save as",
                $"A new name, in the folder of {Path.GetFileName(export.MainFile)}" + (pages ? $"\nOne {extension[1..].ToUpperInvariant()} per page: the name, -1, -2 …" : string.Empty),
                "Name:", stem);
            problem = string.Empty;
            if (name == null) continue;
            // (the extension of the format is added: typed too, it is not doubled)
            if (name.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) name = name[..^extension.Length].TrimEnd();
            if (FileNames.Problem(name + extension) is { } wrong)
            {
                problem = wrong + ". Another name?";
                continue;
            }

            export.Rename(name);
        }

        return true;
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

    // (only for what is shown: the note of the box, and which panes are read again. Paths that differ in case only are
    // taken as the same on every system: at worst a pane is read again, or the note names the file)
    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);
}
