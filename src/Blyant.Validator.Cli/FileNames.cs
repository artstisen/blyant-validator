namespace Blyant.Validator.Cli;

/// <summary>
/// What is wrong with a name typed for a new file: the same rules on every system (the characters and names Windows
/// does not take are refused everywhere, so a folder can be copied to any system). Adapted from Blyant's
/// Tools.FileNameProblem.
/// </summary>
internal static class FileNames
{
    private const string ReservedCharacters = "\\/:*?\"<>|";

    // The names Windows keeps for devices, also with an extension ("con.txt")
    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>Why a name cannot be the name of a file, or null when it can.</summary>
    public static string? Problem(string name)
    {
        if (name.Length == 0 || name is "." or "..") return "Not a file name";
        foreach (var c in name)
        {
            if (char.IsControl(c)) return "A name cannot contain control characters";
            if (ReservedCharacters.Contains(c)) return $"A name cannot contain the character {c}";
        }

        if (name.EndsWith('.') || name.EndsWith(' ')) return "A name cannot end with a dot or a space";
        if (name.Length > 255) return "A name can be at most 255 characters long";
        var stem = name.Split('.')[0].TrimEnd();
        if (ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
            return $"{stem.ToUpperInvariant()} is a name kept by Windows and cannot be used";
        return null;
    }
}
