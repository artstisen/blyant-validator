using System.Runtime.InteropServices;
using System.Text;

namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// Text sent to the system clipboard through the terminal, with the escape sequence OSC 52 (as ":path" in Blyant's
/// file browser). No other program is started: the terminal puts the text on the clipboard.
/// Adapted from Blyant's SystemClipboard.cs (without the paste bin and the string memory of Blyant's editor).
/// </summary>
internal static class Clipboard
{
    /// <summary>
    /// The most bytes of text sent to the clipboard of the terminal (in base64 about 100 KB)
    /// </summary>
    internal const int MaxOsc52Bytes = 75_000;

    /// <summary>
    /// The escape sequence that puts text on the system clipboard of the terminal (OSC 52): the text in base64
    /// </summary>
    internal static string Osc52Sequence(string text) =>
        "\u001b]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) + "\u0007";

    /// <summary>
    /// Copy a text, and tell what was done: the message for the bottom line
    /// </summary>
    internal static string Copy(string text, string what)
    {
        // Most terminals drop a sequence of more than about 100 KB, and writing it could hold the terminal
        if (Encoding.UTF8.GetByteCount(text) > MaxOsc52Bytes)
            return $"Too long for the clipboard of the terminal (more than {MaxOsc52Bytes / 1000} KB): not copied";
        // The text console of Linux has no clipboard, and an older one shows the sequence as text
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("TERM") == "linux")
            return "The text console of Linux has no clipboard: not copied";
        WriteOsc52(text);
        // (a terminal that is known to drop it is not told as "copied")
        return TerminalWithoutClipboard
            ? "This terminal may not take the clipboard (OSC 52)"
            : $"Copied to the clipboard: {what}";
    }

    /// <summary>
    /// True in a terminal that is known to drop the clipboard sequence (OSC 52): the text console of Linux, the
    /// terminals made with VTE (LXTerminal, GNOME Terminal) and the Terminal of macOS
    /// </summary>
    private static bool TerminalWithoutClipboard =>
        _terminalWithoutClipboard ??= !OperatingSystem.IsWindows() && WithoutClipboard(Environment.GetEnvironmentVariable);

    private static bool? _terminalWithoutClipboard;

    // The terminals that take the clipboard sequence, by their TERM, and the variables they set of their own
    private static readonly string[] ClipboardTerms = ["xterm-kitty", "wezterm", "alacritty", "foot", "contour"];
    private static readonly string[] ClipboardMarkers = ["KITTY_WINDOW_ID", "WEZTERM_EXECUTABLE", "ALACRITTY_SOCKET"];

    /// <summary>
    /// The terminal drops the clipboard sequence, as told by the variables of the environment (looked up with
    /// "variable"). VTE_VERSION is passed on to all that is started in a VTE terminal, also another terminal (kitty
    /// started from GNOME Terminal) and tmux, which can pass the sequence on: it only tells of VTE when nothing tells
    /// of one of those
    /// </summary>
    internal static bool WithoutClipboard(Func<string, string?> variable)
    {
        var term = variable("TERM");
        if (term == "linux" || variable("TERM_PROGRAM") == "Apple_Terminal") return true;
        if (string.IsNullOrEmpty(variable("VTE_VERSION"))) return false;
        if (!string.IsNullOrEmpty(variable("TMUX")) || !string.IsNullOrEmpty(variable("TERM_PROGRAM"))) return false;
        if (term != null && ClipboardTerms.Any(name => term.StartsWith(name, StringComparison.Ordinal))) return false;
        return !ClipboardMarkers.Any(marker => !string.IsNullOrEmpty(variable(marker)));
    }

    // Write the escape sequence to the terminal. On Windows the console only reads escape sequences when "virtual
    // terminal processing" is on: it is turned on while the sequence is written, as it was before after
    private static void WriteOsc52(string text)
    {
        var sequence = Osc52Sequence(text);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var handle = GetStdHandle(StdOutputHandle);
                var hasMode = GetConsoleMode(handle, out var mode);
                if (hasMode) SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
                Console.Out.Write(sequence);
                Console.Out.Flush();
                if (hasMode) SetConsoleMode(handle, mode);
            }
            else
            {
                Console.Out.Write(sequence);
                Console.Out.Flush();
            }
        }
        catch
        {
            // Without a terminal there is no clipboard to send to
        }
    }

    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr handle, uint mode);
}
