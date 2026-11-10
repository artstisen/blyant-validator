using Blyant.Validator.Cli;

namespace Blyant.Validator.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blyant-validator-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsTests() => Settings.DirectoryOverride = _dir;

    public void Dispose()
    {
        Settings.DirectoryOverride = null;
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void NoFile_GivesTheDefaults()
    {
        var settings = Settings.Load();
        Assert.Equal(string.Empty, settings.BrowseDirectory1);
        Assert.Equal(0, settings.BrowsePane);
        Assert.True(settings.BrowseDual);
    }

    [Fact]
    public void Settings_AreWrittenAndReadBack()
    {
        var written = new Settings
        {
            BrowseDirectory1 = Path.Combine(_dir, "site"),
            BrowseDirectory2 = Path.Combine(_dir, "a=b"),
            BrowseSelected1 = "index.html",
            BrowseSelected2 = "",
            BrowsePane = 1,
            BrowseDual = false,
        };
        Assert.Null(written.Save());

        var text = File.ReadAllText(Settings.FilePath);
        Assert.StartsWith("; Last modified ", text);
        Assert.Contains("[SETTINGS]\n", text);
        Assert.Contains("browsepane=2\n", text);

        var read = Settings.Load();
        Assert.Equal(written.BrowseDirectory1, read.BrowseDirectory1);
        Assert.Equal(written.BrowseDirectory2, read.BrowseDirectory2);
        Assert.Equal("index.html", read.BrowseSelected1);
        Assert.Equal(1, read.BrowsePane);
        Assert.False(read.BrowseDual);
    }

    [Fact]
    public void Parse_SkipsCommentsSectionsAndUnknownLines()
    {
        var values = Settings.Parse("; comment\r\n[SETTINGS]\r\n# also a comment\r\nbrowsedir1=C:\\x=y\r\nnot a setting\r\n BROWSEPANE = 2 \r\n");
        Assert.Equal(@"C:\x=y", values["browsedir1"]);
        Assert.Equal(" 2", values["browsepane"]);
        Assert.Equal(2, values.Count);
    }
}
