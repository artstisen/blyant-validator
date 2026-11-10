using System.Text;
using Blyant.Validator.Cli.Tui.Terminal;
using Blyant.Validator.Vnu;

namespace Blyant.Validator.Cli.Tui;

/// <summary>
/// The TUI of Blyant Validator: a file browser with one or two panes that shows the result of each HTML and CSS file,
/// and a peek of the selected file in the other pane with its problems marked. Adapted from Blyant's file browser
/// (FileExplorer.cs, MIT License, same author): the panes, keys and drawing follow it.
/// Drawing is in Browser.Draw.cs, the peek in Browser.Peek.cs, and the commands and formatting in Browser.Commands.cs.
/// </summary>
internal static partial class Browser
{
    private const int ListTop = 3; // The first row of the list
    private const string ParentName = "..";

    private sealed record Item(string Name, string Path, bool IsDirectory, long Size);

    // A list of the content of a directory
    private sealed class Pane
    {
        internal string Directory = string.Empty; // Empty = the list of drives (Windows)
        internal List<Item> Items = [];
        internal int Index; // The selected item
        internal int Top; // The first item shown
        internal string Filter = string.Empty;
        internal int ShownTop = -1; // What is on screen: the first item shown and the item selected
        internal int ShownIndex = -1;
        internal readonly HashSet<string> Marks = []; // The paths of the marked files
        internal string MarksDirectory = string.Empty; // The directory the marks were made in
        internal DateTime DirectoryWritten; // When the directory changed last, when it was read
    }

    private static readonly Pane[] Panes = [new(), new()];
    private static int _active; // The pane with the focus
    private static bool _dualPane = true;
    private static bool IsDual => _dualPane;
    private static Pane ActivePane => Panes[_active];
    private static Pane OtherPane => Panes[1 - _active];

    private static string _message = string.Empty;
    private static bool _showAll; // All files are listed, not only HTML and CSS
    private static ResultStore _results = null!;
    private static int PageSize => Screen.LineCount - ListTop - 2;

    /// <summary>
    /// Show the TUI in a directory. Returns when it is closed
    /// </summary>
    public static int Run(string directory, string engine, VnuOptions vnu)
    {
        var encoding = Console.OutputEncoding;
        var controlC = false;
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            controlC = Console.TreatControlCAsInput;
            Console.TreatControlCAsInput = true;
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        {
            // A terminal that cannot be set up keeps what it has
        }

        _results = new ResultStore(engine, vnu);
        Keyboard.Idle = Idle;
        try
        {
            var start = Path.GetFullPath(directory);
            foreach (var pane in Panes)
            {
                pane.Directory = start;
                LoadItems(pane, null);
            }

            Loop();
        }
        finally
        {
            Keyboard.Idle = null;
            _results.Dispose();
            Tools.ShowCursor();
            ConsoleColors.Reset();
            Console.Clear();
            try
            {
                Console.TreatControlCAsInput = controlC;
                Console.OutputEncoding = encoding;
            }
            catch (Exception e) when (e is IOException or PlatformNotSupportedException)
            {
                // As it was set
            }
        }

        return 0;
    }

    private static long _nextDiskCheck;

    // While waiting for a key: results that came in, and (once a second) files and directories changed on disk
    private static bool Idle()
    {
        var changed = _results.TakeChanged();
        if (Environment.TickCount64 < _nextDiskCheck) return changed;
        _nextDiskCheck = Environment.TickCount64 + 1000;
        foreach (var pane in IsDual ? Panes : [ActivePane])
        {
            if (pane.Directory.Length == 0) continue;
            try
            {
                if (System.IO.Directory.GetLastWriteTimeUtc(pane.Directory) != pane.DirectoryWritten)
                {
                    Reload(pane);
                    changed = true;
                    continue;
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            // A file shown that was changed is validated again (Get asks for it)
            var end = Math.Min(pane.Items.Count, pane.Top + PageSize);
            for (var i = pane.Top; i < end; i++)
            {
                if (!pane.Items[i].IsDirectory) _results.Get(pane.Items[i].Path);
            }
        }

        if (_peeking && PeekChanged()) changed = true;
        return changed;
    }

    private static void Loop()
    {
        Screen.ApplyAutoSize();
        Draw(true);
        if (Tools.TakeRedrawRequest()) Draw(true);

        while (true)
        {
            var key = Keyboard.ReadKey();
            if (Keyboard.IsRedraw(key))
            {
                Screen.ApplyAutoSize();
                Draw(true);
                if (Tools.TakeRedrawRequest()) Draw(true);
                continue;
            }

            if (Keyboard.IsWake(key))
            {
                DrawContent();
                if (Tools.TakeRedrawRequest()) Draw(true);
                continue;
            }

            if (Keyboard.IsControl(key, ConsoleKey.C)) break;

            // Moving the selection only redraws the selection pointer (unless the list has to scroll)
            var redraw = true;
            var hadMessage = _message.Length > 0;
            _message = string.Empty;

            // A key that uses the other pane ends the peek first
            if (_peeking && !KeepsPeek(key)) StopPeek();

            if (key.KeyChar == ':')
            {
                var command = CommandPrompt.Read(':', CommandTable);
                if (command != null && RunCommand(command) == CommandResult.Quit) break;
            }
            else if (key.KeyChar == '?')
            {
                HelpViewer.Show(HelpRows);
            }
            else if (key.KeyChar == '/')
            {
                // Only show the names containing a text
                ActivePane.Filter = CommandPrompt.Read('/') ?? string.Empty;
                LoadItems(ActivePane, null);
            }
            else if (key.KeyChar == 'G')
            {
                ActivePane.Index = ActivePane.Items.Count - 1;
                redraw = false;
            }
            else if (key.KeyChar == 'g')
            {
                if (Console.ReadKey(true).KeyChar == 'g') ActivePane.Index = 0;
                redraw = false;
            }
            else if (key.Key == ConsoleKey.Escape)
            {
                // Esc ends the peek, removes the marks, and then a filter, before it closes the program
                if (_peeking) StopPeek();
                else if (ActivePane.Marks.Count > 0) ActivePane.Marks.Clear();
                else if (ActivePane.Filter.Length > 0)
                {
                    ActivePane.Filter = string.Empty;
                    LoadItems(ActivePane, null);
                }
                else break;
            }
            else if (key.Key == ConsoleKey.Q)
            {
                break;
            }
            else if (key.Key is ConsoleKey.J or ConsoleKey.DownArrow)
            {
                ActivePane.Index = Math.Min(ActivePane.Index + 1, Math.Max(0, ActivePane.Items.Count - 1));
                redraw = false;
            }
            else if (key.Key is ConsoleKey.K or ConsoleKey.UpArrow)
            {
                ActivePane.Index = Math.Max(0, ActivePane.Index - 1);
                redraw = false;
            }
            else if (key.Key == ConsoleKey.PageDown || Keyboard.IsControl(key, ConsoleKey.D) ||
                     Keyboard.IsControl(key, ConsoleKey.F))
            {
                ActivePane.Index += PageSize;
                redraw = false;
            }
            else if (key.Key == ConsoleKey.PageUp || Keyboard.IsControl(key, ConsoleKey.U) ||
                     Keyboard.IsControl(key, ConsoleKey.B))
            {
                ActivePane.Index -= PageSize;
                redraw = false;
            }
            else if (key.Key is ConsoleKey.Home)
            {
                ActivePane.Index = 0;
                redraw = false;
            }
            else if (key.Key is ConsoleKey.End)
            {
                ActivePane.Index = ActivePane.Items.Count - 1;
                redraw = false;
            }
            else if (key.Key is ConsoleKey.H or ConsoleKey.LeftArrow or ConsoleKey.Backspace || key.KeyChar == '-')
            {
                GoToParent();
            }
            else if (key.Key is ConsoleKey.L or ConsoleKey.RightArrow or ConsoleKey.Enter)
            {
                if (SelectedItem() is { } item) Open(item);
            }
            else if (key.Key == ConsoleKey.Tab)
            {
                // Into the peek, or the focus to the other pane
                if (_peeking) FocusPeek();
                else if (IsDual) _active = 1 - _active;
                redraw = _peeking;
            }
            else if (key.Key == ConsoleKey.T)
            {
                TogglePanes();
            }
            else if (key.Key == ConsoleKey.V)
            {
                TogglePeek(PeekMode.Text);
            }
            else if (key.Key == ConsoleKey.P)
            {
                TogglePeek(PeekMode.Problems);
            }
            else if (key.Key == ConsoleKey.F)
            {
                FormatFiles();
            }
            else if (key.Key == ConsoleKey.Spacebar)
            {
                ToggleMark();
            }
            else if (key.Key == ConsoleKey.R)
            {
                RefreshPane(ActivePane);
            }

            // Follow the size of the terminal window, and draw again if the page could not be drawn
            if (Screen.ApplyAutoSize()) redraw = true;
            Draw(redraw || hadMessage);
            if (Tools.TakeRedrawRequest()) Draw(true);
        }
    }

    private static Item? SelectedItem() =>
        ActivePane.Items.Count > 0 && ActivePane.Index < ActivePane.Items.Count ? ActivePane.Items[ActivePane.Index] : null;

    // Enter on a directory goes into it; on a file it opens the peek with the focus in it
    private static void Open(Item item)
    {
        if (item.IsDirectory)
        {
            if (item.Name == ParentName) GoToParent();
            else ChangeDirectory(item.Path, null);
            return;
        }

        if (!_peeking) TogglePeek(PeekMode.Text);
        Draw(true);
        FocusPeek();
    }

    // Space: mark the selected file (or unmark it), and select the next one. The marked files are formatted together
    private static void ToggleMark()
    {
        if (SelectedItem() is not { IsDirectory: false } item) return;
        if (!ActivePane.Marks.Remove(item.Path)) ActivePane.Marks.Add(item.Path);
        ActivePane.Index = Math.Min(ActivePane.Index + 1, ActivePane.Items.Count - 1);
    }

    // Show one pane or two
    private static void TogglePanes()
    {
        if (_peeking) StopPeek();
        _dualPane = !_dualPane;
        Reload(OtherPane);
    }

    // r: read the directory again, and validate its files again
    private static void RefreshPane(Pane pane)
    {
        Reload(pane);
        foreach (var item in pane.Items)
        {
            if (!item.IsDirectory && ResultStore.IsValidated(item.Path)) _results.Refresh(item.Path);
        }

        _message = "Checking the files again";
    }

    // Read the content of the directory of a pane again, keeping the selection
    private static void Reload(Pane pane)
    {
        var selected = pane.Items.Count > 0 && pane.Index < pane.Items.Count ? pane.Items[pane.Index] : null;
        var index = pane.Index;
        var top = pane.Top;
        var message = _message;
        if (!LoadItems(pane, null))
        {
            // The directory is gone
            pane.Directory = HomeDirectory();
            pane.Filter = string.Empty;
            LoadItems(pane, null);
            _message = message;
            return;
        }

        var found = selected == null ? -1 : pane.Items.FindIndex(item => item.Name == selected.Name);
        pane.Index = found >= 0 ? found : Math.Min(index, Math.Max(0, pane.Items.Count - 1));
        pane.Top = top;
        pane.ShownTop = -1;
    }

    private static string HomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return home.Length > 0 && System.IO.Directory.Exists(home) ? home : Path.GetFullPath(".");
    }

    private static void ChangeDirectory(string directory, string? select)
    {
        var previous = ActivePane.Directory;
        ActivePane.Directory = directory;
        ActivePane.Filter = string.Empty;
        if (!LoadItems(ActivePane, select))
        {
            // Stay where we were if the directory cannot be read
            ActivePane.Directory = previous;
            var message = _message;
            LoadItems(ActivePane, null);
            _message = message;
        }
    }

    private static void GoToParent()
    {
        if (ActivePane.Directory.Length == 0) return;
        var parent = System.IO.Directory.GetParent(ActivePane.Directory)?.FullName;
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(ActivePane.Directory));
        if (parent != null)
        {
            ChangeDirectory(parent, name);
        }
        else if (OperatingSystem.IsWindows())
        {
            // Above the root of a drive is the list of drives
            ChangeDirectory(string.Empty, ActivePane.Directory);
        }
    }

    // Read the content of the directory of a pane: directories first, then files (HTML and CSS, or all with ":all"),
    // both sorted by name
    private static bool LoadItems(Pane pane, string? select)
    {
        var items = new List<Item>();
        try
        {
            if (pane.Directory.Length == 0)
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.IsReady) items.Add(new Item(drive.Name, drive.Name, true, 0));
                }
            }
            else
            {
                items.Add(new Item(ParentName, string.Empty, true, 0));
                var info = new DirectoryInfo(pane.Directory);
                pane.DirectoryWritten = info.LastWriteTimeUtc;
                foreach (var directory in info.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (IsShown(directory, pane.Filter)) items.Add(new Item(directory.Name, directory.FullName, true, 0));
                }

                foreach (var file in info.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if (IsShown(file, pane.Filter) && (_showAll || ResultStore.IsValidated(file.Name)))
                        items.Add(new Item(file.Name, file.FullName, false, file.Length));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _message = e.Message;
            return false;
        }

        pane.Items = items;
        // The marks are kept while the same directory is shown (also with a filter). The ones gone are dropped
        if (pane.MarksDirectory != pane.Directory) pane.Marks.Clear();
        pane.Marks.RemoveWhere(path => !File.Exists(path));
        pane.MarksDirectory = pane.Directory;
        pane.Index = Math.Max(0, select == null ? 0 : items.FindIndex(i => i.Name == select || i.Path == select));
        pane.Top = 0;
        pane.ShownTop = -1;
        return true;
    }

    private static bool IsShown(FileSystemInfo info, string filter)
    {
        if ((info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return false;
        if (info.Name.StartsWith('.')) return false;
        return filter.Length == 0 || info.Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
