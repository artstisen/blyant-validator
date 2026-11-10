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
                foreach (var directory in Folders(path, recursive))
                {
                    files.AddRange(Patterns
                        .SelectMany(p => Directory.EnumerateFiles(directory, p, options))
                        .Order(StringComparer.OrdinalIgnoreCase));
                }
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

    /// <summary>
    /// A folder and (when recursive) the folders in it: those starting with "." (.git, .vs) and node_modules are passed
    /// over, and so are links to folders (a link can lead round in a circle), and folders that cannot be read
    /// </summary>
    private static IEnumerable<string> Folders(string root, bool recursive)
    {
        yield return root;
        if (!recursive) yield break;
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var pending = new Stack<string>([root]);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            List<string> subfolders;
            try
            {
                subfolders = [.. Directory.EnumerateDirectories(folder, "*", options)
                    .Where(d => Path.GetFileName(d) is var name && !name.StartsWith('.') && name != "node_modules")
                    .Order(StringComparer.OrdinalIgnoreCase)];
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var subfolder in subfolders)
            {
                yield return subfolder;
                pending.Push(subfolder);
            }
        }
    }
}
