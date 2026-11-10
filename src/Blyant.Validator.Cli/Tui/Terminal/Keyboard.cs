using System.Runtime.InteropServices;

namespace Blyant.Validator.Cli.Tui.Terminal;

/// <summary>
/// Reading keys, and noticing that the terminal window was resized while waiting for one.
/// Adapted from Blyant's Keyboard.cs.
/// </summary>
internal static class Keyboard
{
    public static ConsoleKeyInfo ToNavigationKey(ConsoleKeyInfo key)
    {
        var arrow = key.KeyChar switch
        {
            'h' => ConsoleKey.LeftArrow,
            'j' => ConsoleKey.DownArrow,
            'k' => ConsoleKey.UpArrow,
            'l' => ConsoleKey.RightArrow,
            _ => ConsoleKey.None
        };
        return arrow == ConsoleKey.None ? key : new ConsoleKeyInfo('\0', arrow, false, false, false);
    }

    /// <summary>
    /// True if the key is CTRL + a letter (and not a character typed with AltGr, which is reported as CTRL+ALT)
    /// </summary>
    public static bool IsControl(ConsoleKeyInfo key, ConsoleKey letter)
    {
        return key.Key == letter && (key.Modifiers & ConsoleModifiers.Control) != 0 &&
               (key.Modifiers & ConsoleModifiers.Alt) == 0;
    }

    /// <summary>
    /// The key a page gets when it must be drawn again (the window was resized). It does nothing on a page: the page
    /// is drawn again as after any key
    /// </summary>
    internal static readonly ConsoleKeyInfo RedrawKey = new('\0', ConsoleKey.F24, false, false, false);

    internal static bool IsRedraw(ConsoleKeyInfo key) => key.Key == ConsoleKey.F24 && key.KeyChar == '\0';

    /// <summary>
    /// The key a page gets when something it shows has changed while waiting (results of the validation came in, a
    /// file changed on disk). It does nothing on a page: the page is drawn again
    /// </summary>
    internal static readonly ConsoleKeyInfo WakeKey = new('\0', ConsoleKey.F23, false, false, false);

    internal static bool IsWake(ConsoleKeyInfo key) => key.Key == ConsoleKey.F23 && key.KeyChar == '\0';

    /// <summary>
    /// Asked while waiting for a key, a few times a second: true when the page should be drawn again (WakeKey)
    /// </summary>
    internal static Func<bool>? Idle { get; set; }

    /// <summary>
    /// The next key of a page. While waiting, the size of the terminal window is looked at: when it has another size
    /// and has kept it for a moment (a window dragged larger passes many sizes), RedrawKey comes, and the page is drawn
    /// again in the dimensions that follow the window. WakeKey comes when Idle says so
    /// </summary>
    internal static ConsoleKeyInfo ReadKey()
    {
        var shown = WindowSize((0, 0)); // The size of the window the page was drawn in
        var seen = shown; // A new size, and when it was seen first
        var seenAt = 0L;
        while (true)
        {
            try
            {
                if (Console.KeyAvailable) return Console.ReadKey(true);
            }
            catch (InvalidOperationException)
            {
                // Keys cannot be asked about: wait for one
                return Console.ReadKey(true);
            }

            if (Idle?.Invoke() == true) return WakeKey;

            var size = WindowSize(shown);
            if (size == shown)
            {
                seenAt = 0;
            }
            else if (seenAt == 0 || size != seen)
            {
                seen = size;
                seenAt = Environment.TickCount64;
            }
            else if (Environment.TickCount64 - seenAt >= SettleMilliseconds)
            {
                return RedrawKey;
            }

            WaitForInput();
        }
    }

    /// <summary>
    /// How long the window keeps its new size before a page follows it
    /// </summary>
    internal const int SettleMilliseconds = 150;

    // How long to wait between the questions for a key and the size of the window (on Windows: at most, as a key
    // ends the wait)
    private const int KeyWaitMilliseconds = 15;
    private const int WindowsWaitMilliseconds = 100;

    /// <summary>
    /// The size of the terminal window (the one given, if it is not known)
    /// </summary>
    internal static (int Width, int Height) WindowSize((int Width, int Height) unknown)
    {
        try
        {
            return (Console.WindowWidth, Console.WindowHeight);
        }
        catch
        {
            return unknown;
        }
    }

    /// <summary>
    /// Wait until input may have come, or a while has passed. On Windows the input of the console is waited for (a
    /// key ends the wait at once); elsewhere there is a short pause
    /// </summary>
    private static void WaitForInput()
    {
        if (OperatingSystem.IsWindows())
        {
            var input = GetStdHandle(StdInputHandle);
            if (input != IntPtr.Zero && input != new IntPtr(-1) &&
                WaitForSingleObject(input, WindowsWaitMilliseconds) != WaitFailed)
            {
                return;
            }
        }

        Thread.Sleep(KeyWaitMilliseconds);
    }

    private const int StdInputHandle = -10;
    private const uint WaitFailed = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
}
