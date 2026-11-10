namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// The styles text is written in, in the colors of Blyant's Default color scheme.
/// Adapted from Blyant's ConsoleColors.cs and ColorScheme.cs (one built-in scheme, no black-and-white mode).
/// </summary>
internal static class ConsoleColors
{
    // Blyant's Default color scheme (Schemes/Default.txt). The background is that of the terminal
    private const ConsoleColor Text = ConsoleColor.Gray;
    private const ConsoleColor Logo = ConsoleColor.Cyan;
    private const ConsoleColor Dividers = ConsoleColor.DarkGray;
    private const ConsoleColor Accent = ConsoleColor.Yellow;
    private const ConsoleColor Highlight = ConsoleColor.Cyan;
    private const ConsoleColor HighlightText = ConsoleColor.Black;
    private const ConsoleColor Attention = ConsoleColor.Yellow;
    private const ConsoleColor AttentionText = ConsoleColor.Black;
    private const ConsoleColor Warning = ConsoleColor.Red;
    private const ConsoleColor WarningText = ConsoleColor.White;
    private const ConsoleColor BoxBorders = ConsoleColor.White;
    private const ConsoleColor Tab = ConsoleColor.Black;
    private const ConsoleColor TabText = ConsoleColor.Cyan;
    private const ConsoleColor KeyHints = ConsoleColor.Gray;
    private const ConsoleColor LineNumbers = ConsoleColor.DarkGray;
    private const ConsoleColor CurrentLine = ConsoleColor.White;
    private const ConsoleColor Selector = ConsoleColor.Cyan;
    private const ConsoleColor SelectorText = ConsoleColor.Black;
    private const ConsoleColor CommandLine = ConsoleColor.Cyan;
    private const ConsoleColor CommandLineText = ConsoleColor.Black;
    private const ConsoleColor CommandError = ConsoleColor.Yellow;
    private const ConsoleColor CommandErrorText = ConsoleColor.Black;

    // Syntax 1 to 12 of the Default scheme
    private static readonly ConsoleColor[] Syntax =
    [
        ConsoleColor.Cyan, ConsoleColor.Yellow, ConsoleColor.Green, ConsoleColor.Magenta, ConsoleColor.Green,
        ConsoleColor.DarkGray, ConsoleColor.Red, ConsoleColor.Magenta, ConsoleColor.Yellow, ConsoleColor.Cyan,
        ConsoleColor.Green, ConsoleColor.Blue,
    ];

    /// <summary>
    /// True when the terminal says it has 8 colors (TERM=linux, the text console of Linux and a few others): .NET then
    /// shows the bright colors as the dark ones, so they are set again with the codes those terminals have for them
    /// </summary>
    private static bool EightColors =>
        _eightColors ??= !OperatingSystem.IsWindows() && HasEightColors(Environment.GetEnvironmentVariable("TERM"));

    private static bool? _eightColors;

    private static bool HasEightColors(string? term) =>
        term is "linux" or "xterm" or "screen" or "tmux" or "vt100" or "vt220" or "ansi" or "rxvt" or "xterm-color" ||
        term?.StartsWith("linux", StringComparison.Ordinal) == true;

    // The colors of the terminal (ANSI) in the order of ConsoleColor: black, blue, green, cyan, red, magenta,
    // yellow, white
    private static readonly int[] Ansi = [0, 4, 2, 6, 1, 5, 3, 7];

    private static string? BrightCode(ConsoleColor color, bool background)
    {
        var value = (int)color;
        if (value < 8) return null;
        return "\u001b[" + ((background ? 100 : 90) + Ansi[value - 8]) + "m";
    }

    private static ConsoleColor? _foreground;
    private static ConsoleColor? _background;

    private static void SetForeground(ConsoleColor color)
    {
        _foreground = color;
        Console.ForegroundColor = color;
        WriteBrightCodes();
    }

    private static void SetBackground(ConsoleColor color)
    {
        _background = color;
        Console.BackgroundColor = color;
        WriteBrightCodes();
    }

    private static void WriteBrightCodes()
    {
        if (!EightColors) return;
        if (_foreground is { } foreground && BrightCode(foreground, false) is { } fore) Console.Write(fore);
        if (_background is { } background && BrightCode(background, true) is { } back) Console.Write(back);
    }

    /// <summary>
    /// Back to the colors of the terminal
    /// </summary>
    internal static void Reset()
    {
        _foreground = null;
        _background = null;
        Console.ResetColor();
    }

    public enum ColorPreset
    {
        Normal,
        HeaderColor,
        Divider,
        Accent,
        HighlightInverted,
        AttentionInverted,
        WarningInverted,
        BoxBorder,
        BoxCorner,
        KeyHint,
        KeyHintInverted,
        LineNumber,
        CurrentLineNumber,
        SelectorInverted,
        CommandLineInverted,
        CommandErrorInverted,
        BoxTitle,
        Error, // A problem of a file: error
        Warning, // A problem of a file: warning
        Info, // A problem of a file: info
        Ok, // A file without problems
    }

    /// <summary>
    /// Write in a style
    /// </summary>
    public static void SetColors(ColorPreset preset)
    {
        switch (preset)
        {
            case ColorPreset.HighlightInverted:
                Inverted(Highlight, HighlightText);
                return;
            case ColorPreset.AttentionInverted:
                Inverted(Attention, AttentionText);
                return;
            case ColorPreset.WarningInverted:
                Inverted(Warning, WarningText);
                return;
            case ColorPreset.KeyHintInverted:
                Inverted(KeyHints, ConsoleColor.Black);
                return;
            case ColorPreset.SelectorInverted:
                Inverted(Selector, SelectorText);
                return;
            case ColorPreset.CommandLineInverted:
                Inverted(CommandLine, CommandLineText);
                return;
            case ColorPreset.CommandErrorInverted:
                Inverted(CommandError, CommandErrorText);
                return;
            case ColorPreset.BoxTitle:
                Inverted(Tab, TabText);
                return;
        }

        Reset();
        SetForeground(preset switch
        {
            ColorPreset.HeaderColor => Logo,
            ColorPreset.Divider => Dividers,
            ColorPreset.Accent => Accent,
            ColorPreset.BoxBorder or ColorPreset.BoxCorner => BoxBorders,
            ColorPreset.KeyHint => KeyHints,
            ColorPreset.LineNumber => LineNumbers,
            ColorPreset.CurrentLineNumber => CurrentLine,
            ColorPreset.Error => ConsoleColor.Red,
            ColorPreset.Warning => ConsoleColor.Yellow,
            ColorPreset.Info => ConsoleColor.Cyan,
            ColorPreset.Ok => ConsoleColor.Green,
            _ => Text,
        });
    }

    private static void Inverted(ConsoleColor background, ConsoleColor text)
    {
        SetBackground(background);
        SetForeground(text);
    }

    /// <summary>
    /// Write colored text of a file in its color (Syntax 1 to 12 of the color scheme, counted from 0)
    /// </summary>
    internal static void SetSyntaxColor(int number)
    {
        Reset();
        SetForeground(Syntax[number]);
    }
}
