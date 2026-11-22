using System.Collections.Frozen;

namespace Blyant.Validator.Core;

/// <summary>Paths compared as the disk has them (the engines ask the disk only through the functions they are given).</summary>
public static class DiskPaths
{
    /// <summary>
    /// True when two full paths are the same place on the disk. Paths that differ only in the case of letters are, unless
    /// a folder on the way holds both spellings (a file system that tells case apart: Linux, and some macOS volumes): the
    /// folders are asked, not the system. <paramref name="namesIn"/> gives the names of the files and folders in a folder,
    /// as they are written, and none when the folder cannot be read; then the answer is <paramref name="whenUnknown"/>
    /// </summary>
    public static bool SamePlace(string a, string b, Func<string, IReadOnlyList<string>> namesIn, bool whenUnknown = true)
    {
        if (string.Equals(a, b, StringComparison.Ordinal)) return true;
        if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        var folderA = Path.GetDirectoryName(a);
        var folderB = Path.GetDirectoryName(b);
        if (folderA is null || folderB is null) return true; // Roots: "C:\" and "c:\"
        if (!SamePlace(folderA, folderB, namesIn, whenUnknown)) return false;
        var nameA = Path.GetFileName(a);
        var nameB = Path.GetFileName(b);
        if (string.Equals(nameA, nameB, StringComparison.Ordinal)) return true;
        var names = namesIn(folderA);
        if (names.Count == 0) return whenUnknown;
        var set = names.ToFrozenSet(StringComparer.Ordinal);
        return !(set.Contains(nameA) && set.Contains(nameB));
    }
}
