namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// The text of a field of one line being typed (a file name in ChoiceBox.AskText), and where the cursor is in it. It
/// does not touch the console: ChoiceBox draws it. A character is a whole element (an emoji is two chars), as in the
/// command line. Written for Blyant Validator, where Blyant uses its editor (TextArea) of one line.
/// </summary>
internal sealed class LineEdit(string text)
{
    public string Text { get; private set; } = text;

    /// <summary>Where the cursor is: the index in Text it is in front of.</summary>
    public int Cursor { get; private set; } = text.Length;

    /// <summary>Handle a key; returns false for a key it does not use.</summary>
    public bool Handle(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.LeftArrow:
                if (Cursor > 0) Cursor -= TextMetrics.PreviousElementLength(Text, Cursor);
                return true;
            case ConsoleKey.RightArrow:
                if (Cursor < Text.Length) Cursor += TextMetrics.ElementLength(Text, Cursor);
                return true;
            case ConsoleKey.Home:
                Cursor = 0;
                return true;
            case ConsoleKey.End:
                Cursor = Text.Length;
                return true;
            case ConsoleKey.Backspace:
                if (Cursor > 0)
                {
                    var length = TextMetrics.PreviousElementLength(Text, Cursor);
                    Text = Text.Remove(Cursor - length, length);
                    Cursor -= length;
                }

                return true;
            case ConsoleKey.Delete:
                if (Cursor < Text.Length) Text = Text.Remove(Cursor, TextMetrics.ElementLength(Text, Cursor));
                return true;
        }

        if (char.IsControl(key.KeyChar) || key.KeyChar == '\0') return false;
        Text = Text.Insert(Cursor, key.KeyChar.ToString());
        Cursor++;
        return true;
    }

    /// <summary>
    /// What is shown in a field of a width (in columns), scrolled so that the cursor is in it, and the column of the
    /// cursor in the field
    /// </summary>
    public (string Shown, int Column) View(int width)
    {
        width = Math.Max(2, width);
        var start = 0;
        // (the cursor takes a column after the text)
        while (start < Cursor && TextMetrics.WidthOfRange(Text, start, Cursor) > width - 1)
            start += TextMetrics.ElementLength(Text, start);
        return (TextMetrics.StartPart(Text[start..], width), TextMetrics.WidthOfRange(Text, start, Cursor));
    }
}
