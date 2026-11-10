namespace Blyant.Validator.Cli;

/// <summary>Expands the paths given on the command line into a list of files.</summary>
internal static class FileCollector
{
    private static readonly string[] Patterns = ["*.html", "*.htm", "*.xhtml", "*.css"];

    public static List<string> Collect(IEnumerable<string> paths, bool recursive, TextWriter errors)
    {
        var files = new List<string>();
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                files.Add(path);
            }
            else if (Directory.Exists(path))
            {
                files.AddRange(Patterns
                    .SelectMany(p => Directory.EnumerateFiles(path, p, option))
                    .Order(StringComparer.OrdinalIgnoreCase));
            }
            else
            {
                errors.WriteLine($"blyant-validator: '{path}' does not exist.");
            }
        }

        return files.Distinct().ToList();
    }
}
