using Blyant.Validator.Cli.Tui;
using Blyant.Validator.Vnu;

namespace Blyant.Validator.Cli;

internal static class BrowseCommand
{
    /// <summary>
    /// Open the TUI in a folder, or (null) where it was left last. It needs a terminal: with input or output
    /// redirected it is refused
    /// </summary>
    public static int Run(string? folder, string engine, string? vnuJar, string? java)
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine($"{CommandLine.Program}: the TUI needs a terminal (input or output is redirected).");
            return 2;
        }

        if (folder != null && !Directory.Exists(folder))
        {
            Console.Error.WriteLine($"{CommandLine.Program}: '{folder}' is not a directory.");
            return 2;
        }

        return Browser.Run(folder, engine, new VnuOptions(vnuJar, java));
    }
}
