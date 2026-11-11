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
/// package names that are not valid. The document of a file is that of its main file (the file that includes it, and
/// the file that includes that …, looked for in its folder and the folders above), with all the files it includes or
/// imports: a chapter may refer to a label of another.
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
            if (files.Exists == null || file.Path.Length == 0 || Resolve(folder, file.Path, root: null) is not { } full) continue;
            if (!files.Exists(full))
                report(file.Offset, Severity.Error, "typst.missing-file", $"The file “{file.Path}” is not there.");
        }
    }

    /// <summary>
    /// A path in a Typst file, from the folder of the file ("\" is read as "/" on every system). A path that starts
    /// with "/" is from the root of the project: from <paramref name="root"/>, and null when the root is not known (the
    /// drive is never taken from where the program runs). Null too for a path that cannot be one (a NUL in it)
    /// </summary>
    private static string? Resolve(string folder, string path, string? root)
    {
        path = path.Replace('\\', '/');
        if (path.StartsWith('/'))
        {
            if (root == null) return null;
            (folder, path) = (root, path.TrimStart('/'));
        }

        try
        {
            return Path.GetFullPath(path, folder);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    // How many folders the files that include a file are looked for in: its own and the folders above it
    private const int Levels = 4;

    /// <summary>
    /// The Typst files around a file, in the same order on every system: those of its folder first, then of each folder
    /// above it (up to <see cref="Levels"/> folders), each by name
    /// </summary>
    private static IEnumerable<string> Around(string full, TypstFiles files)
    {
        var folders = new List<string>();
        for (var folder = Path.GetDirectoryName(full); folder != null && folders.Count < Levels; folder = Path.GetDirectoryName(folder))
            folders.Add(folder);
        return folders.SelectMany(f => files.TypstFilesIn!(f).Select(Path.GetFullPath).Order(StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal).Take(MaxFiles);
    }

    /// <summary>
    /// True when two full paths are the same file. Paths that differ only in the case of letters are, unless the folder
    /// has both names (a file system that tells case apart: on Linux, and on some macOS volumes): asked of the folder, not
    /// of the system
    /// </summary>
    internal static bool SamePath(string a, string b, TypstFiles files)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        if (files.TypstFilesIn == null || Path.GetDirectoryName(a) is not { } folder) return true;
        var names = files.TypstFilesIn(folder).Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        return !(names.Contains(Path.GetFileName(a)) && names.Contains(Path.GetFileName(b)));
    }

    // The full paths of the files of a document: the same file in any case of letters is read once (a file system that
    // tells case apart rarely has two such files, and then a check is left out rather than wrong)
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

    // References (@name) to labels that are not in the document, or that are there more than once
    private static void CheckReferences(string path, TypstReader reader, TypstFiles files, Action<int, Severity, string, string> report)
    {
        // (a label in code that places it any number of times, #if or #let, is not counted)
        foreach (var twice in reader.Labels.Where(l => !l.InCode).GroupBy(l => l.Name, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            foreach (var label in twice.Skip(1))
                report(label.Offset, Severity.Warning, "typst.duplicate-label",
                    $"The label “<{label.Name}>” is in this file more than once: a reference to it is an error.");
        }

        if (reader.References.Count == 0) return;

        // The labels of the whole document. When it cannot be known (labels made from strings, a bibliography whose
        // keys are referred to with @, a package, files included by code, or files that cannot be read), the references
        // are not checked
        if (Document(Path.GetFullPath(path), reader, files) is not { } document) return;
        foreach (var reference in reader.References)
        {
            var count = document.Counted.GetValueOrDefault(reference.Name);
            if (count == 0 && document.Present.Contains(reference.Name)) continue;
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

    /// <summary>
    /// The labels of the document of a file. Counted: how many times each is placed in it, in the main file the file is
    /// in that has the most (two main files, a book and a handout, each have it once). Present: the labels that are in
    /// it but are not counted (in code that places them any number of times, in imported files, whose content is not
    /// placed as it is), and when no file includes the file, the labels of the Typst files beside it (it may be put in
    /// its document elsewhere, with them).
    /// </summary>
    private sealed record Labels(Dictionary<string, int> Counted, HashSet<string> Present);

    // The labels of the document a file is in, or null when they cannot be known
    private static Labels? Document(string full, TypstReader reader, TypstFiles files)
    {
        var parsed = new ParsedFiles(files);
        parsed.Add(full, reader);
        if (Includers(full, parsed) is not { } includers) return null;

        var counted = new Dictionary<string, int>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        if (includers.Count == 0)
        {
            // The file is its own document; the files beside it may be in the same document elsewhere
            if (Tally(full, parsed, counted, present) == false) return null;
            var folder = Path.GetDirectoryName(full);
            foreach (var other in files.TypstFilesIn == null || folder == null ? [] : files.TypstFilesIn(folder))
            {
                if (parsed.Get(Path.GetFullPath(other)) is { } beside) present.UnionWith(beside.Labels.Select(l => l.Name));
            }

            return new Labels(counted, present);
        }

        // Each main file the file is in (through the files that include it, and the files that include those)
        var mains = new HashSet<string>(Paths);
        foreach (var includer in includers)
        {
            if (Top(includer, parsed) is not { } main) return null;
            if (mains.Add(main) && !Tally(main, parsed, counted, present)) return null;
        }

        return new Labels(counted, present);
    }

    // Count the labels of the document of a main file into those found so far (keeping the highest count of each);
    // false when its files cannot all be known
    private static bool Tally(string main, ParsedFiles parsed, Dictionary<string, int> counted, HashSet<string> present)
    {
        var document = new Dictionary<string, bool>(Paths); // The files of the document, and whether one is imported
        // (a path from the root is from the folder of the main file: the root Typst takes when none is given)
        if (!Follow(main, imported: false, Path.GetDirectoryName(main) ?? ".", parsed, document)) return false;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (file, imported) in document)
        {
            var reader = parsed.Get(file)!;
            if (reader.HasDynamicLabels || reader.HasDynamicIncludes) return false;
            foreach (var label in reader.Labels)
            {
                present.Add(label.Name);
                if (!imported && !label.InCode) counts[label.Name] = counts.GetValueOrDefault(label.Name) + 1;
            }
        }

        foreach (var (name, count) in counts) counted[name] = Math.Max(count, counted.GetValueOrDefault(name));
        return true;
    }

    // Add a file of a document and the files it includes or imports, and theirs; false when one cannot be read, a
    // package is imported (its labels are not known), or there are too many
    private static bool Follow(string full, bool imported, string root, ParsedFiles parsed, Dictionary<string, bool> document)
    {
        if (document.TryGetValue(full, out var before))
        {
            // (a file both included and imported is placed)
            if (before && !imported) document[full] = false;
            return true;
        }

        if (document.Count >= MaxFiles || parsed.Get(full) is not { } reader) return false;
        document[full] = imported;
        var folder = Path.GetDirectoryName(full) ?? ".";
        foreach (var file in reader.Paths.Where(p => p.Function is "include" or "import"))
        {
            if (file.Path.StartsWith('@') || Resolve(folder, file.Path, root) is not { } other) return false;
            if (!Follow(other, imported || file.Function == "import", root, parsed, document)) return false;
        }

        return true;
    }

    /// <summary>
    /// The main file of the document a file is in: the file that includes it, and the file that includes that … (looked
    /// for in the folder of each and the folders above); the file itself when no file includes it
    /// </summary>
    public static string MainFile(string path, TypstFiles files) => Top(Path.GetFullPath(path), new ParsedFiles(files)) ?? Path.GetFullPath(path);

    // The top of the files that include a file (the first of them at each step), or null when a file around it includes
    // files by code (which may be this one)
    private static string? Top(string full, ParsedFiles parsed)
    {
        var seen = new HashSet<string>(Paths) { full };
        var current = full;
        while (true)
        {
            if (Includers(current, parsed) is not { } includers) return null;
            if (includers.Count == 0 || !seen.Add(includers[0])) return current;
            current = includers[0];
        }
    }

    // The Typst files around a file that include it, in the order of Around; null when one of them includes files by
    // code (it may include this one)
    private static List<string>? Includers(string full, ParsedFiles parsed)
    {
        var files = parsed.Files;
        if (files.TypstFilesIn == null || files.Read == null) return [];
        var result = new List<string>();
        foreach (var other in Around(full, files))
        {
            if (SamePath(other, full, files) || parsed.Get(other) is not { } reader) continue;
            if (reader.HasDynamicIncludes) return null;
            var folder = Path.GetDirectoryName(other) ?? ".";
            // (by a path from its folder, or from its folder as the root of the project, as when it is the main file)
            if (reader.Paths.Any(p => p.Function == "include" && !p.Path.StartsWith('@') &&
                                      Resolve(folder, p.Path, folder) is { } included && SamePath(included, full, files)))
                result.Add(other);
        }

        return result;
    }

    // The Typst files read for one check, each read and parsed once
    private sealed class ParsedFiles(TypstFiles files)
    {
        private readonly Dictionary<string, TypstReader?> _readers = new(Paths);

        public TypstFiles Files => files;

        public void Add(string full, TypstReader reader) => _readers[full] = reader;

        public TypstReader? Get(string full)
        {
            if (_readers.TryGetValue(full, out var reader)) return reader;
            if (_readers.Count >= MaxFiles * 2) return null;
            return _readers[full] = files.Read?.Invoke(full) is { } text ? TypstReader.Read(text) : null;
        }
    }

    // @namespace/name:1.2.3
    [GeneratedRegex(@"^@[^\s/:@]+/[^\s/:@]+:[0-9]+\.[0-9]+\.[0-9]+$")]
    private static partial Regex Package();
}
