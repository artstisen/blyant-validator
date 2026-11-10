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
/// What a folder changes of the rules it gets from the folders above it: fragment mode on or off (null: as above),
/// codes it ignores more, and codes it ignores no longer.
/// </summary>
internal sealed record FolderRule(bool? Fragment, IReadOnlyList<string> Ignore, IReadOnlyList<string> Unignore)
{
    public bool IsEmpty => Fragment is null && Ignore.Count == 0 && Unignore.Count == 0;

    /// <summary>The rules of the folder, from those of the folders above.</summary>
    public CheckRules Apply(CheckRules above) =>
        new(Fragment ?? above.Fragment, [.. above.Ignore.Except(Unignore).Concat(Ignore.Except(above.Ignore)).Distinct()]);

    /// <summary>What a folder has to change of the rules above to get the rules wanted.</summary>
    public static FolderRule Between(CheckRules above, CheckRules wanted) =>
        new(wanted.Fragment == above.Fragment ? null : wanted.Fragment,
            [.. wanted.Ignore.Except(above.Ignore)],
            [.. above.Ignore.Except(wanted.Ignore)]);
}

/// <summary>
/// The check rules of the TUI per folder (<c>:fragment</c>, <c>:ignore</c>), kept in settings.ini. The rules of a
/// folder apply to its subfolders too: a subfolder keeps only what it changes of them, so a later change of the folder
/// above still reaches it. Read on the background thread of the results and changed on the main thread, so it locks.
/// It does not touch the console or the disk, so it can be tested.
/// </summary>
internal sealed class FolderRules
{
    // Names of files are case-sensitive on Linux, not on Windows and (by default) macOS
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, FolderRule> _folders = new(PathComparer);
    private readonly Lock _lock = new();

    /// <summary>The folders with rules of their own, and what they change.</summary>
    public IReadOnlyList<KeyValuePair<string, FolderRule>> Folders
    {
        get
        {
            lock (_lock) return [.. _folders.OrderBy(f => f.Key, PathComparer)];
        }
    }

    /// <summary>The rules of the files in a folder: those of the folders above it, with what each one changes.</summary>
    public CheckRules ForDirectory(string directory)
    {
        var folder = Normalize(directory);
        var chain = new List<string>();
        for (var current = folder; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)) chain.Add(current);

        var rules = CheckRules.Full;
        lock (_lock)
        {
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                if (_folders.TryGetValue(chain[i], out var rule)) rules = rule.Apply(rules);
            }
        }

        return rules;
    }

    /// <summary>The rules of a file: those of its folder.</summary>
    public CheckRules ForFile(string path) => ForDirectory(Path.GetDirectoryName(path) ?? string.Empty);

    /// <summary>
    /// Set the rules of a folder (for it and its subfolders). Only what differs from the rules it gets from the folders
    /// above is kept
    /// </summary>
    public void Set(string directory, CheckRules rules)
    {
        var folder = Normalize(directory);
        if (folder.Length == 0) return;
        var parent = Path.GetDirectoryName(folder);
        var above = parent is null ? CheckRules.Full : ForDirectory(parent);
        var rule = FolderRule.Between(above, rules);
        lock (_lock)
        {
            if (rule.IsEmpty) _folders.Remove(folder);
            else _folders[folder] = rule;
        }
    }

    // A full path without a separator at the end (except a root: "C:\", "/")
    private static string Normalize(string directory)
    {
        if (directory.Length == 0) return string.Empty;
        var full = Path.GetFullPath(directory);
        return Path.GetPathRoot(full) == full ? full : Path.TrimEndingDirectorySeparator(full);
    }

    /// <summary>
    /// Read the rules from the settings, numbered from 1: "checkdir1=C:\Docs", "checkfragment1=true" (or false; empty:
    /// as the folders above), "checkignore1=css.*,html.x" and "checkunignore1=html.y". Codes that are not valid are
    /// left out
    /// </summary>
    public static FolderRules Read(IReadOnlyDictionary<string, string> values)
    {
        var result = new FolderRules();
        string Get(string name) => values.TryGetValue(name, out var value) ? value.Trim() : string.Empty;
        IReadOnlyList<string> Codes(string name) => [.. IgnoreList.Split(Get(name)).Where(IgnoreList.IsValid)];
        for (var n = 1; Get("checkdir" + n) is { Length: > 0 } folder; n++)
        {
            var fragment = Get("checkfragment" + n).ToLowerInvariant() switch
            {
                "true" => true,
                "false" => (bool?)false,
                _ => null,
            };
            var rule = new FolderRule(fragment, Codes("checkignore" + n), Codes("checkunignore" + n));
            try
            {
                if (!rule.IsEmpty) result._folders[Normalize(folder)] = rule;
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
        foreach (var (folder, rule) in Folders)
        {
            n++;
            yield return $"checkdir{n}={folder}";
            yield return $"checkfragment{n}={rule.Fragment switch { true => "true", false => "false", null => "" }}";
            yield return $"checkignore{n}={string.Join(',', rule.Ignore)}";
            yield return $"checkunignore{n}={string.Join(',', rule.Unignore)}";
        }
    }
}
