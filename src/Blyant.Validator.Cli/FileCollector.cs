namespace Blyant.Validator.Cli;

/// <summary>Expands the paths given on the command line into a list of files.</summary>
internal static class FileCollector
{
    private static readonly string[] Patterns = ["*.html", "*.htm", "*.xhtml", "*.css", "*.xml", "*.svg", "*.rss", "*.atom", "*.md", "*.markdown", "llms.txt", "llms-full.txt"];

    /// <param name="missing">How many of the paths do not exist (each told about in <paramref name="errors"/>).</param>
    public static List<string> Collect(IEnumerable<string> paths, bool recursive, TextWriter errors, out int missing)
    {
        missing = 0;
        var files = new List<string>();
        // Names are matched without regard to case on every system (INDEX.HTML is HTML on Linux too), and folders that
        // cannot be read are passed over
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = true,
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
            else if (Directory.Exists(path))
            {
                files.AddRange(Patterns
                    .SelectMany(p => Directory.EnumerateFiles(path, p, options))
                    .Order(StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                errors.WriteLine($"blyant-validator: '{path}' does not exist.");
                missing++;
            }
        }

        // A file given twice (by itself and in its folder) is done once
        var seen = new HashSet<string>(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        return [.. files.Where(f => seen.Add(Path.GetFullPath(f)))];
    }
}
