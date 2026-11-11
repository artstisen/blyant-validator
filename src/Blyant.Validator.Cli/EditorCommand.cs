using System.Text;

namespace Blyant.Validator.Cli;

/// <summary>
/// The editor files are opened in from the TUI (":editor", the setting "editor"): a program and its arguments, as the
/// user typed them. "{file}" in an argument is the file, and "{line}" the line to open it at; without "{file}" the
/// file is the last argument. The program is started directly, never through a shell, so a file name is never read
/// as a command: a .cmd or .bat (which Windows can only run with cmd.exe) is refused.
/// </summary>
internal sealed class EditorCommand
{
    private EditorCommand(string text, string program, IReadOnlyList<string> arguments)
    {
        Text = text;
        Program = program;
        Arguments = arguments;
    }

    /// <summary>The command as typed (and kept in the settings).</summary>
    public string Text { get; }

    public string Program { get; }

    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Read a command: words between blanks, or between double quotes ("C:\Program Files\Neovim\bin\nvim.exe" +{line}).
    /// Null when there is no program, or the quotes are not closed
    /// </summary>
    public static EditorCommand? Parse(string text)
    {
        text = text.Trim();
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        var quoted = false;
        foreach (var c in text)
        {
            if (c == '"')
            {
                quoted = !quoted;
                inWord = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (inWord) words.Add(word.ToString());
                word.Clear();
                inWord = false;
            }
            else
            {
                word.Append(c);
                inWord = true;
            }
        }

        if (quoted) return null;
        if (inWord) words.Add(word.ToString());
        if (words.Count == 0 || words[0].Length == 0) return null;
        return new EditorCommand(text, words[0], words.Skip(1).ToList());
    }

    /// <summary>The arguments for a file, opened at a line (1 when it is not known).</summary>
    public IReadOnlyList<string> ArgumentsFor(string file, int line)
    {
        var lineText = Math.Max(1, line).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var result = Arguments.Select(a => a.Replace("{file}", file, StringComparison.Ordinal)
            .Replace("{line}", lineText, StringComparison.Ordinal)).ToList();
        if (!Arguments.Any(a => a.Contains("{file}", StringComparison.Ordinal))) result.Add(file);
        return result;
    }

    /// <summary>
    /// The full path of the program: the path given, or found on PATH (on Windows also with ".exe"). Null when it is
    /// not there
    /// </summary>
    public string? Resolve()
    {
        string[] names = OperatingSystem.IsWindows() && Path.GetExtension(Program).Length == 0 ? [Program + ".exe", Program] : [Program];
        if (Path.IsPathRooted(Program) || Program.Contains('/') || Program.Contains(Path.DirectorySeparatorChar))
            return names.Select(Path.GetFullPath).FirstOrDefault(DocumentIo.IsFile);
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(folder.Trim('"'), name);
                    if (DocumentIo.IsFile(candidate)) return Path.GetFullPath(candidate);
                }
                catch (ArgumentException)
                {
                    // A folder on PATH that is not a path
                }
            }
        }

        return null;
    }

    /// <summary>Why the command cannot be used, or null when it can.</summary>
    public string? Problem(string? resolved)
    {
        if (resolved == null) return $"Not found: {Program}";
        var extension = Path.GetExtension(resolved);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) || extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            return $"{Path.GetFileName(resolved)} is run by cmd.exe: give the editor's program (.exe) instead";
        return null;
    }

    /// <summary>
    /// True for a Windows program with windows of its own (Notepad, VS Code), false for a program of the console (Vim,
    /// Blyant) or when it cannot be told: read from the header of the .exe (its subsystem)
    /// </summary>
    public static bool IsWindowsGui(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D) return false; // "MZ"
            stream.Position = 0x3C;
            var header = reader.ReadInt32();
            // ("PE\0\0", the file header of 20 bytes, and the subsystem at 68 in the optional header)
            if (header < 0 || header + 24 + 70 > stream.Length) return false;
            stream.Position = header;
            if (reader.ReadUInt32() != 0x00004550) return false;
            stream.Position = header + 24 + 68;
            return reader.ReadUInt16() == 2; // IMAGE_SUBSYSTEM_WINDOWS_GUI
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or EndOfStreamException)
        {
            return false;
        }
    }
}
