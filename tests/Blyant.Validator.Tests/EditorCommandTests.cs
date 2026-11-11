using Blyant.Validator.Cli;

namespace Blyant.Validator.Tests;

/// <summary>The editor of the TUI (:editor, O): the command is read, and the file put in its arguments</summary>
public sealed class EditorCommandTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blyant-validator-editor-" + Guid.NewGuid().ToString("N"));

    public EditorCommandTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void TheFile_IsTheLastArgument_UnlessItsPlaceIsMarked()
    {
        var file = Path.Combine(_dir, "a page.html");
        Assert.Equal(["--wait", file], EditorCommand.Parse("code --wait")!.ArgumentsFor(file, 12));
        Assert.Equal(["+12", file], EditorCommand.Parse("nvim +{line} {file}")!.ArgumentsFor(file, 12));
        Assert.Equal(["--goto", file + ":1"], EditorCommand.Parse("code --goto {file}:{line}")!.ArgumentsFor(file, 0));
        Assert.Equal([file], EditorCommand.Parse("  vim  ")!.ArgumentsFor(file, 3));
    }

    [Fact]
    public void QuotedWords_KeepTheirBlanks()
    {
        var editor = EditorCommand.Parse("\"C:\\Program Files\\Microsoft VS Code\\Code.exe\" --wait \"a b\"");
        Assert.NotNull(editor);
        Assert.Equal("C:\\Program Files\\Microsoft VS Code\\Code.exe", editor.Program);
        Assert.Equal(["--wait", "a b"], editor.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"nvim")]
    [InlineData("\"\" --wait")]
    public void NoProgram_IsNoEditor(string text)
    {
        Assert.Null(EditorCommand.Parse(text));
    }

    [Fact]
    public void AProgram_IsFoundByItsPath_OrNotFound()
    {
        var program = Path.Combine(_dir, "my editor");
        File.WriteAllText(program, "");
        var editor = EditorCommand.Parse($"\"{program}\" -n")!;
        Assert.Equal(program, editor.Resolve());
        Assert.Null(editor.Problem(editor.Resolve()));

        var missing = EditorCommand.Parse($"\"{Path.Combine(_dir, "none")}\"")!;
        Assert.Null(missing.Resolve());
        Assert.StartsWith("Not found", missing.Problem(missing.Resolve()), StringComparison.Ordinal);
        Assert.Null(EditorCommand.Parse("no-such-editor-" + Guid.NewGuid().ToString("N"))!.Resolve());
    }

    [Theory]
    [InlineData("code.cmd")]
    [InlineData("edit.BAT")]
    public void BatchFiles_AreRefused_AsTheyNeedAShell(string name)
    {
        var program = Path.Combine(_dir, name);
        File.WriteAllText(program, "");
        var editor = EditorCommand.Parse($"\"{program}\"")!;
        Assert.Contains("cmd.exe", editor.Problem(editor.Resolve()), StringComparison.Ordinal);
    }

    [WindowsFact]
    public void WindowsPrograms_AreToldFromConsolePrograms()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        Assert.True(EditorCommand.IsWindowsGui(Path.Combine(system, "notepad.exe")));
        Assert.False(EditorCommand.IsWindowsGui(Path.Combine(system, "cmd.exe")));
        var text = Path.Combine(_dir, "not a program.exe");
        File.WriteAllText(text, "MZ but nothing more");
        Assert.False(EditorCommand.IsWindowsGui(text));
        Assert.False(EditorCommand.IsWindowsGui(Path.Combine(_dir, "none.exe")));
    }
}
