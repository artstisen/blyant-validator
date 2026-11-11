using System.Text.RegularExpressions;
using Blyant.Validator.Core;

namespace Blyant.Validator.Typst;

/// <summary>
/// What the validator may ask of the files around a Typst file (the validator itself does not touch the file system):
/// whether a file is there, the text of a file, and the Typst files of a folder. A function left out (null) means
/// that check is not made.
/// </summary>
public sealed record TypstFiles(
    Func<string, bool>? Exists = null,
    Func<string, string?>? Read = null,
    Func<string, IReadOnlyList<string>>? TypstFilesIn = null);

/// <summary>
/// Native Typst validator: what the Typst compiler refuses and that can be told without compiling (see
/// <see cref="TypstReader"/>), references to labels that are not in the document, files that are not there, and
/// package names that are not valid. The document of a file is the file, the files it includes or imports, and the
/// files that include it (looked for in its folder and the folder above): a chapter may refer to a label of another.
/// Code is not evaluated, and nothing is fetched: a package is only checked for its form.
/// </summary>
public sealed partial class TypstValidator(TypstFiles? files = null) : IValidator
{
    // The most files a document is read from (a project of chapters is far below)
    private const int MaxFiles = 200;

    public string Engine => "native";

    public bool CanValidate(DocumentKind kind) => kind == DocumentKind.Typst;

    public Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default) =>
        Task.FromResult(Validate(document.Path, document.Content, files));

    public static IReadOnlyList<Diagnostic> Validate(string path, string text, TypstFiles? files = null)
    {
        files ??= new TypstFiles();
        var diagnostics = new List<Diagnostic>();
        var lines = new LineMap(text);
        var reader = TypstReader.Read(text);

        void Report(int offset, Severity severity, string code, string message)
        {
            var at = lines[offset];
            diagnostics.Add(new Diagnostic(path, severity, code, message, at, at));
        }

        foreach (var problem in reader.Problems)
            Report(problem.Offset, problem.IsError ? Severity.Error : Severity.Warning, problem.Code, problem.Message);

        CheckPaths(path, reader, files, Report);
        CheckReferences(path, reader, files, Report);
        return [.. diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column)];
    }

    // The files given to #include, #import, image(), read(), bibliography() …: there, or a package of a valid name
    private static void CheckPaths(string path, TypstReader reader, TypstFiles files, Action<int, Severity, string, string> report)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        foreach (var file in reader.Paths)
        {
            if (file.Path.StartsWith('@'))
            {
                if (file.Function is "import" or "include" && !Package().IsMatch(file.Path))
                    report(file.Offset, Severity.Error, "typst.bad-package",
                        $"“{file.Path}” is not a package: a package is named as “@preview/name:1.0.0”.");
                continue;
            }

            // (a path from the root of the project: the root is not known here)
            if (files.Exists == null || file.Path.Length == 0 || file.Path.StartsWith('/') || file.Path.StartsWith('\\')) continue;
            var full = Resolve(folder, file.Path);
            if (!files.Exists(full))
                report(file.Offset, Severity.Error, "typst.missing-file", $"The file “{file.Path}” is not there.");
        }
    }

    // A path in a Typst file, from the folder of the file ("\" is read as "/" on every system)
    private static string Resolve(string folder, string path) => Path.GetFullPath(Path.Combine(folder, path.Replace('\\', '/')));

    // References (@name) to labels that are not in the document, or that are there more than once
    private static void CheckReferences(string path, TypstReader reader, TypstFiles files, Action<int, Severity, string, string> report)
    {
        foreach (var twice in reader.Labels.GroupBy(l => l.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            foreach (var label in twice.Skip(1))
                report(label.Offset, Severity.Warning, "typst.duplicate-label",
                    $"The label “<{label.Name}>” is in this file more than once: a reference to it is an error.");
        }

        if (reader.References.Count == 0) return;

        // The labels of the whole document. When it cannot be known (labels made from strings, a bibliography whose
        // keys are referred to with @, a package, or files that cannot be read), the references are not checked
        if (Document(path, reader, files) is not var (document, nearby)) return;
        foreach (var reference in reader.References)
        {
            var count = document.GetValueOrDefault(reference.Name);
            // (a file that no file around it includes may be put in its document elsewhere, with the files beside it)
            if (count == 0 && nearby.Contains(reference.Name)) continue;
            if (count == 0)
            {
                report(reference.Offset, Severity.Error, "typst.unknown-reference", reference.InWord
                    ? $"“@{reference.Name}” is read as a reference to a label, which is not in the document. Write \\@ for an at sign (as in an e-mail address)."
                    : $"There is no label “<{reference.Name}>” in the document.");
            }
            else if (count > 1)
            {
                report(reference.Offset, Severity.Error, "typst.ambiguous-reference",
                    $"The label “<{reference.Name}>” is in the document more than once: the reference cannot tell which.");
            }
        }
    }

    // The labels of the document a file is in, with how many times each is there, and (when no file around it
    // includes it) the labels of the Typst files beside it; null when the labels cannot be known
    private static (Dictionary<string, int> Labels, HashSet<string> Nearby)? Document(string path, TypstReader reader, TypstFiles files)
    {
        var nearby = new HashSet<string>(StringComparer.Ordinal);
        var included = false;
        var full = Path.GetFullPath(path);
        var readers = new Dictionary<string, TypstReader>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)
        {
            [full] = reader,
        };

        // The files it includes, and the files that include it (in its folder and the folder above)
        if (!Follow(full, reader, files, readers)) return null;
        if (files.TypstFilesIn != null && files.Read != null)
        {
            var folder = Path.GetDirectoryName(full);
            var parent = folder == null ? null : Path.GetDirectoryName(folder);
            foreach (var candidate in new[] { folder, parent }.OfType<string>().SelectMany(files.TypstFilesIn).Distinct().Take(MaxFiles))
            {
                var other = Path.GetFullPath(candidate);
                if (readers.ContainsKey(other) || files.Read(other) is not { } text) continue;
                var otherReader = TypstReader.Read(text);
                var otherFolder = Path.GetDirectoryName(other) ?? ".";
                if (!otherReader.Paths.Any(p => p.Function == "include" && !p.Path.StartsWith('@') &&
                                                readers.Comparer.Equals(Resolve(otherFolder, p.Path), full)))
                {
                    if (readers.Comparer.Equals(otherFolder, folder ?? "")) nearby.UnionWith(otherReader.Labels.Select(l => l.Name));
                    continue;
                }

                included = true;
                readers[other] = otherReader;
                if (!Follow(other, otherReader, files, readers)) return null;
            }
        }

        if (readers.Values.Any(r => r.HasDynamicLabels)) return null;
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var label in readers.Values.SelectMany(r => r.Labels))
            labels[label.Name] = labels.GetValueOrDefault(label.Name) + 1;
        if (included) nearby.Clear();
        return (labels, nearby);
    }

    /// <summary>
    /// The main file of the document a file is in: the file that includes it, and the file that includes that … (looked
    /// for in the folder of each and the folder above); the file itself when no file includes it
    /// </summary>
    public static string MainFile(string path, TypstFiles files)
    {
        var current = Path.GetFullPath(path);
        var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase) { current };
        while (Includer(current, files) is { } includer && seen.Add(includer)) current = includer;
        return current;
    }

    // A Typst file in the folder of a file or the folder above that includes it, or null
    private static string? Includer(string full, TypstFiles files)
    {
        if (files.TypstFilesIn == null || files.Read == null) return null;
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var folder = Path.GetDirectoryName(full);
        var parent = folder == null ? null : Path.GetDirectoryName(folder);
        foreach (var candidate in new[] { folder, parent }.OfType<string>().SelectMany(files.TypstFilesIn).Distinct().Take(MaxFiles))
        {
            var other = Path.GetFullPath(candidate);
            if (comparer.Equals(other, full) || files.Read(other) is not { } text) continue;
            var otherFolder = Path.GetDirectoryName(other) ?? ".";
            if (TypstReader.Read(text).Paths.Any(p => p.Function == "include" && !p.Path.StartsWith('@') &&
                                                       comparer.Equals(Resolve(otherFolder, p.Path), full)))
                return other;
        }

        return null;
    }

    // Read the Typst files a file includes or imports, and theirs; false when one cannot be read, or a package is
    // imported (its labels are not known)
    private static bool Follow(string full, TypstReader reader, TypstFiles files, Dictionary<string, TypstReader> readers)
    {
        var folder = Path.GetDirectoryName(full) ?? ".";
        foreach (var file in reader.Paths.Where(p => p.Function is "include" or "import"))
        {
            if (file.Path.StartsWith('@') || file.Path.StartsWith('/') || file.Path.StartsWith('\\')) return false;
            var other = Resolve(folder, file.Path);
            if (readers.ContainsKey(other)) continue;
            if (readers.Count >= MaxFiles || files.Read?.Invoke(other) is not { } text) return false;
            var otherReader = TypstReader.Read(text);
            readers[other] = otherReader;
            if (!Follow(other, otherReader, files, readers)) return false;
        }

        return true;
    }

    // @namespace/name:1.2.3
    [GeneratedRegex(@"^@[^\s/:@]+/[^\s/:@]+:[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex Package();
}
