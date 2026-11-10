using Blyant.Validator.Cli;

namespace Blyant.Validator.Tests;

public class CommandLineTests
{
    private static readonly CommandSpec Command = new("format", "Format files.",
    [
        new("--recursive", "-r", "Recurse."),
        new("--engine", "-e", "Engine.", OptionKind.Text, Choices: ["native", "vnu"], Default: "native"),
        new("--indent-size", null, "Indent.", OptionKind.Number, ValueName: "<n>", Default: "2", Min: 1, Max: 16),
    ], "<paths>", "Files.");

    [Fact]
    public void Defaults_AreUsed()
    {
        var parsed = CommandLine.Parse(Command, ["a.css"]);
        Assert.Equal(["a.css"], parsed.Arguments);
        Assert.False(parsed.Flag("--recursive"));
        Assert.Equal("native", parsed.Text("--engine"));
        Assert.Equal(2, parsed.Number("--indent-size"));
    }

    [Theory]
    [InlineData("-r", "-e", "vnu", "--indent-size", "4")]
    [InlineData("--recursive", "--engine=VNU", "--indent-size=4")]
    public void OptionsAndAliases_AreRead(params string[] options)
    {
        var parsed = CommandLine.Parse(Command, [.. options, "a.css", "b.html"]);
        Assert.True(parsed.Flag("--recursive"));
        Assert.Equal("vnu", parsed.Text("--engine"));
        Assert.Equal(4, parsed.Number("--indent-size"));
        Assert.Equal(["a.css", "b.html"], parsed.Arguments);
    }

    [Fact]
    public void DoubleDash_EndsTheOptions()
    {
        var parsed = CommandLine.Parse(Command, ["--", "-odd-name.css"]);
        Assert.Equal(["-odd-name.css"], parsed.Arguments);
    }

    [Fact]
    public void Help_NeedsNoArguments()
    {
        Assert.True(CommandLine.Parse(Command, ["--help"]).Help);
        Assert.Contains("--indent-size <n>", CommandLine.CommandHelp(Command));
    }

    [Theory]
    [InlineData("--nope", "a.css")]
    [InlineData("--engine", "other", "a.css")]
    [InlineData("--indent-size", "0", "a.css")]
    [InlineData("--indent-size", "x", "a.css")]
    [InlineData("--recursive=yes", "a.css")]
    [InlineData("a.css", "--engine")]
    [InlineData("-r")]
    public void Mistakes_AreReported(params string[] args)
    {
        Assert.Throws<UsageException>(() => CommandLine.Parse(Command, args));
    }
}
