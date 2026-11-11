using System.Diagnostics;
using Blyant.Validator.Cli.Tui.Terminal;

namespace Blyant.Validator.Cli.Tui;

// ":editor", ":edit" and O: the selected file opened in an editor of the user's choice (kept in the settings)
internal static partial class Browser
{
    /// <summary>The editor files are opened in (the setting "editor"), or null when none is set.</summary>
    internal static EditorCommand? Editor { get; set; }

    // ":editor command": set the editor (":editor none" takes it away, ":editor" alone tells it)
    private static void SetEditor(string argument)
    {
        if (argument.Length == 0)
        {
            _message = Editor == null ? "No editor: set one with :editor path (and arguments)" : $"Editor: {Editor.Text} (O opens the selected file)";
            return;
        }

        if (argument.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            Editor = null;
            _message = "No editor";
            return;
        }

        if (EditorCommand.Parse(argument) is not { } editor)
        {
            _message = "Not an editor command (a quote that is not closed?)";
            return;
        }

        if (editor.Problem(editor.Resolve()) is { } problem)
        {
            _message = problem;
            return;
        }

        Editor = editor;
        _message = $"Editor: {editor.Text} (O opens the selected file)";
    }

    // O or ":edit": the selected file in the editor, at a line (in the peek: the line of the cursor, or of the problem
    // selected). The TUI gives the terminal to the editor, waits until it is closed, and takes it back. A program with
    // windows of its own (on Windows: Notepad, VS Code) is not waited for
    private static void EditFile(int line)
    {
        if (SelectedItem() is not { IsDirectory: false } item)
        {
            _message = "Select a file";
            return;
        }

        if (Editor is not { } editor)
        {
            _message = "No editor: set one with :editor path (and arguments)";
            return;
        }

        var program = editor.Resolve();
        if (editor.Problem(program) is { } problem)
        {
            _message = problem;
            return;
        }

        var psi = new ProcessStartInfo(program!)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(item.Path) ?? ".",
        };
        foreach (var argument in editor.ArgumentsFor(item.Path, line)) psi.ArgumentList.Add(argument);
        var windowed = OperatingSystem.IsWindows() && EditorCommand.IsWindowsGui(program!);

        string? failed = null;
        if (!windowed) GiveTerminal();
        try
        {
            using var process = Process.Start(psi);
            if (!windowed) process?.WaitForExit();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            failed = $"Could not start {Path.GetFileName(program)}: {e.Message}";
        }
        finally
        {
            if (!windowed) TakeTerminal();
        }

        // (the file is checked again when it was changed: the results follow the time and length of the file)
        _message = failed ?? (windowed ? $"Opened {item.Name} in {Path.GetFileNameWithoutExtension(program)}" : string.Empty);
    }

    // Ctrl+C in the editor is the editor's: it does not stop the TUI
    private static void KeepRunning(object? sender, ConsoleCancelEventArgs e) => e.Cancel = true;

    // The terminal as it was before the TUI, for an editor in the terminal (on Linux and macOS .NET also gives the
    // terminal its own settings back while the editor runs, and takes them again after)
    private static void GiveTerminal()
    {
        Console.CancelKeyPress += KeepRunning;
        ConsoleColors.Reset();
        Console.Clear();
        Tools.ShowCursor();
    }

    // The terminal back for the TUI, drawn anew
    private static void TakeTerminal()
    {
        Console.CancelKeyPress -= KeepRunning;
        Tools.HideCursor();
        Tools.ClearScreen();
        Draw(true);
    }
}
