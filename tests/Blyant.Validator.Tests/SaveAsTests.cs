using Blyant.Validator.Cli;
using Blyant.Validator.Cli.Tui.Terminal;

namespace Blyant.Validator.Tests;

/// <summary>"Save as" of :typst: the field the name is typed in, and the names that are refused</summary>
public sealed class SaveAsTests
{
    private static ConsoleKeyInfo Key(ConsoleKey key) => new('\0', key, false, false, false);

    private static ConsoleKeyInfo Typed(char c) => new(c, ConsoleKey.NoName, false, false, false);

    [Fact]
    public void TheField_IsEdited_WhereTheCursorIs()
    {
        var field = new LineEdit("main");
        Assert.Equal(4, field.Cursor);
        field.Handle(Key(ConsoleKey.Backspace));
        field.Handle(Key(ConsoleKey.Home));
        field.Handle(Typed('x'));
        field.Handle(Key(ConsoleKey.RightArrow));
        field.Handle(Key(ConsoleKey.Delete));
        field.Handle(Key(ConsoleKey.End));
        field.Handle(Typed('!'));
        Assert.Equal("xmi!", field.Text);
        Assert.Equal(4, field.Cursor);
        Assert.False(field.Handle(Key(ConsoleKey.Enter)));
    }

    [Fact]
    public void AnEmoji_IsOneCharacter()
    {
        var field = new LineEdit("a\U0001F600");
        field.Handle(Key(ConsoleKey.LeftArrow));
        Assert.Equal(1, field.Cursor);
        field.Handle(Key(ConsoleKey.Delete));
        Assert.Equal("a", field.Text);
    }

    [Fact]
    public void ALongText_ScrollsToTheCursor()
    {
        var field = new LineEdit(new string('a', 30) + "end");
        var (shown, column) = field.View(10);
        Assert.Equal("aaaaaaend", shown);
        Assert.Equal(9, column);
        field.Handle(Key(ConsoleKey.Home));
        Assert.Equal((new string('a', 10), 0), field.View(10));
    }

    [Theory]
    [InlineData("print.pdf", true)]
    [InlineData("bog – udkast.pdf", true)]
    [InlineData("a/b.pdf", false)]
    [InlineData("a\\b.pdf", false)]
    [InlineData("what?.pdf", false)]
    [InlineData("con.pdf", false)]
    [InlineData("LPT1.png", false)]
    [InlineData("name .pdf", true)]
    [InlineData("name.", false)]
    [InlineData("..", false)]
    [InlineData("tab\there.pdf", false)]
    public void FileNames_AreChecked(string name, bool allowed)
    {
        Assert.Equal(allowed, FileNames.Problem(name) == null);
    }
}
