using System.Globalization;
using System.Text;

namespace Blyant.Validator.Cli;

/// <summary>
/// The settings file of the TUI (settings.ini in the data directory), in the format of Blyant's settings file: a
/// comment with the time it was written, a [SETTINGS] section, and "name=value" lines read by name, so they can be in
/// any order. Missing or invalid settings get their default value, and unknown ones are ignored.
/// Adapted from Blyant's FileIo.cs.
/// </summary>
internal sealed class Settings
{
    internal const string FileName = "settings.ini";
    private const string DataDirectoryName = "data";

    /// <summary>The directories last shown in the two panes (empty: none yet).</summary>
    public string BrowseDirectory1 { get; set; } = string.Empty;
    public string BrowseDirectory2 { get; set; } = string.Empty;

    /// <summary>The names selected in the two panes.</summary>
    public string BrowseSelected1 { get; set; } = string.Empty;
    public string BrowseSelected2 { get; set; } = string.Empty;

    /// <summary>The pane that had the focus: 0 or 1 (written as 1 or 2, as Blyant does).</summary>
    public int BrowsePane { get; set; }

    /// <summary>Two panes side by side (false: one).</summary>
    public bool BrowseDual { get; set; } = true;

    /// <summary>The rows of the panes are numbered (:number), as in Blyant ("filenumbers").</summary>
    public bool FileNumbers { get; set; }

    /// <summary>The folders whose files are not fully checked (:fragment, :ignore).</summary>
    public FolderRules Checks { get; set; } = new();

    /// <summary>The data directory, for tests (so nothing is written next to the program).</summary>
    internal static string? DirectoryOverride { get; set; }

    private static string? _dataDirectory;

    /// <summary>
    /// The data directory, chosen as Blyant chooses its own: "data" in the directory the program is started in, if
    /// there is one there with a settings file in it (a portable setup, and the screenshots of tools/screenshots.py);
    /// else "data" next to the program, so the same settings are used wherever it is started from. If the program
    /// directory cannot be written to (a program installed for all users), "Blyant Validator/data" in the user's
    /// application data is used
    /// </summary>
    internal static string DataDirectory => DirectoryOverride ?? (_dataDirectory ??=
        File.Exists(Path.Combine(DataDirectoryName, FileName)) ? Path.GetFullPath(DataDirectoryName) : FindDataDirectory());

    internal static string FilePath => Path.Combine(DataDirectory, FileName);

    private static string FindDataDirectory()
    {
        var nextToProgram = Path.Combine(AppContext.BaseDirectory, DataDirectoryName);
        var exists = Directory.Exists(nextToProgram);
        if (exists ? CanWriteIn(nextToProgram) : CanWriteIn(AppContext.BaseDirectory)) return nextToProgram;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify), "Blyant Validator", DataDirectoryName);
    }

    // True if a file can be made in a directory (it is made and deleted again)
    private static bool CanWriteIn(string directory)
    {
        try
        {
            var test = Path.Combine(directory, ".blyant-validator-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(test, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Read the settings file. A file that is not there, or cannot be read, gives the default settings
    /// </summary>
    public static Settings Load()
    {
        var settings = new Settings();
        string content;
        try
        {
            content = File.ReadAllText(FilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return settings;
        }

        var values = Parse(content);
        string Get(string name) => values.TryGetValue(name, out var value) ? value.Trim() : string.Empty;
        settings.BrowseDirectory1 = Get("browsedir1");
        settings.BrowseDirectory2 = Get("browsedir2");
        settings.BrowseSelected1 = Get("browsesel1");
        settings.BrowseSelected2 = Get("browsesel2");
        settings.BrowsePane = Get("browsepane") == "2" ? 1 : 0;
        settings.BrowseDual = !Get("browsedual").Equals("false", StringComparison.OrdinalIgnoreCase);
        settings.FileNumbers = Get("filenumbers").Equals("true", StringComparison.OrdinalIgnoreCase);
        settings.Checks = FolderRules.Read(values);
        return settings;
    }

    /// <summary>
    /// Get the "name=value" pairs of a settings file. Sections, comments and blank lines are skipped, and a value is
    /// everything after the first "=" (a path can contain one)
    /// </summary>
    internal static Dictionary<string, string> Parse(string content)
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '[' or ';' or '#') continue;
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            settings[line[..separator].Trim()] = line[(separator + 1)..];
        }

        return settings;
    }

    /// <summary>The text of the settings file.</summary>
    internal string Format()
    {
        var text = new StringBuilder();
        text.Append("; Last modified ")
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("[SETTINGS]\n");
        text.Append('\n');
        text.Append("; FILE BROWSER\n");
        // The directories last shown in the two panes, the names selected in them, and the pane that had the focus
        text.Append("browsedir1=").Append(BrowseDirectory1).Append('\n');
        text.Append("browsedir2=").Append(BrowseDirectory2).Append('\n');
        text.Append("browsesel1=").Append(BrowseSelected1).Append('\n');
        text.Append("browsesel2=").Append(BrowseSelected2).Append('\n');
        text.Append("browsepane=").Append(BrowsePane + 1).Append('\n');
        // Two panes side by side (false: one pane)
        text.Append("browsedual=").Append(BrowseDual ? "true" : "false").Append('\n');
        // The rows of the panes are numbered
        text.Append("filenumbers=").Append(FileNumbers ? "true" : "false").Append('\n');
        text.Append('\n');
        text.Append("; CHECKS\n");
        // Folders (and their subfolders) whose HTML files are fragments, and the problem codes ignored in them
        foreach (var line in Checks.Lines()) text.Append(line).Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// Write the settings file safely (it is never left empty or half written). A failure is returned, not thrown:
    /// the settings are only a convenience
    /// </summary>
    public string? Save()
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            DocumentIo.SafeWriteAllText(FilePath, Format(), withBom: false);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }
}
