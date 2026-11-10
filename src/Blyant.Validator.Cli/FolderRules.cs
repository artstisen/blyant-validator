using Blyant.Validator.Core;

namespace Blyant.Validator.Cli;

/// <summary>How the files of a folder are checked, when it is not a full check.</summary>
/// <param name="Fragment">HTML files are fragments: the content of a &lt;body&gt;, for a page made elsewhere (a CMS).</param>
/// <param name="Ignore">The diagnostic codes ignored (see <see cref="IgnoreList"/>), in the order they were added.</param>
internal sealed record CheckRules(bool Fragment, IReadOnlyList<string> Ignore)
{
    public static CheckRules Full { get; } = new(false, []);

    public bool IsFull => !Fragment && Ignore.Count == 0;

    public bool Equals(CheckRules? other) =>
        other is not null && Fragment == other.Fragment && Ignore.SequenceEqual(other.Ignore);

    public override int GetHashCode() => HashCode.Combine(Fragment, Ignore.Count);
}

/// <summary>
/// The check rules of the TUI per folder (<c>:fragment</c>, <c>:ignore</c>), kept in settings.ini. The rules of a
/// folder apply to its subfolders too, unless a subfolder has rules of its own. Read on the background thread of the
/// results and changed on the main thread, so it locks. It does not touch the console or the disk, so it can be tested.
/// </summary>
internal sealed class FolderRules
{
    // Names of files are case-sensitive on Linux, not on Windows and (by default) macOS
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, CheckRules> _folders = new(PathComparer);
    private readonly Lock _lock = new();

    /// <summary>The folders with rules of their own, and the rules.</summary>
    public IReadOnlyList<KeyValuePair<string, CheckRules>> Folders
    {
        get
        {
            lock (_lock) return [.. _folders.OrderBy(f => f.Key, PathComparer)];
        }
    }

    /// <summary>The rules of the files in a folder: its own, or those of the nearest folder above with rules.</summary>
    public CheckRules ForDirectory(string directory) => Find(directory).Rules;

    /// <summary>The rules of a file: those of its folder.</summary>
    public CheckRules ForFile(string path) => ForDirectory(Path.GetDirectoryName(path) ?? string.Empty);

    /// <summary>The folder the rules of a folder are set in (itself or a folder above), or null if it has none.</summary>
    public string? SetIn(string directory) => Find(directory).Folder;

    /// <summary>
    /// Set the rules of a folder (for it and its subfolders). Rules that are the same as those it would get from the
    /// folders above are not kept, so a folder only has rules of its own when they are different
    /// </summary>
    public void Set(string directory, CheckRules rules)
    {
        var folder = Normalize(directory);
        if (folder.Length == 0) return;
        lock (_lock)
        {
            _folders.Remove(folder);
            var parent = Path.GetDirectoryName(folder);
            var inherited = parent is null ? CheckRules.Full : Find(parent).Rules;
            if (!rules.Equals(inherited)) _folders[folder] = rules;
        }
    }

    private (string? Folder, CheckRules Rules) Find(string directory)
    {
        var folder = Normalize(directory);
        lock (_lock)
        {
            for (var current = folder; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if (_folders.TryGetValue(current, out var rules)) return (current, rules);
            }
        }

        return (null, CheckRules.Full);
    }

    // A full path without a separator at the end (except a root: "C:\", "/")
    private static string Normalize(string directory)
    {
        if (directory.Length == 0) return string.Empty;
        var full = Path.GetFullPath(directory);
        return Path.GetPathRoot(full) == full ? full : Path.TrimEndingDirectorySeparator(full);
    }

    /// <summary>
    /// Read the rules from the settings: "checkdir1=C:\Docs", "checkfragment1=true", "checkignore1=css.*,html.x",
    /// numbered from 1. Codes that are not valid are left out
    /// </summary>
    public static FolderRules Read(IReadOnlyDictionary<string, string> values)
    {
        var result = new FolderRules();
        string Get(string name) => values.TryGetValue(name, out var value) ? value.Trim() : string.Empty;
        for (var n = 1; Get("checkdir" + n) is { Length: > 0 } folder; n++)
        {
            var ignore = IgnoreList.Split(Get("checkignore" + n)).Where(IgnoreList.IsValid).ToList();
            var rules = new CheckRules(Get("checkfragment" + n).Equals("true", StringComparison.OrdinalIgnoreCase), ignore);
            try
            {
                if (!rules.IsFull) result._folders[Normalize(folder)] = rules;
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A path that is not valid here is left out
            }
        }

        return result;
    }

    /// <summary>The lines of the settings file.</summary>
    public IEnumerable<string> Lines()
    {
        var n = 0;
        foreach (var (folder, rules) in Folders)
        {
            n++;
            yield return $"checkdir{n}={folder}";
            yield return $"checkfragment{n}={(rules.Fragment ? "true" : "false")}";
            yield return $"checkignore{n}={string.Join(',', rules.Ignore)}";
        }
    }
}
