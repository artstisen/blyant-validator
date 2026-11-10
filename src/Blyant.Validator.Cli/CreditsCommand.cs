using Blyant.Validator.Core;

namespace Blyant.Validator.Cli;

internal static class CreditsCommand
{
    public static int Run()
    {
        Console.WriteLine($"Blyant Validator {CommandLine.Version}");
        Console.WriteLine("A companion to the Blyant editor: https://blyant-editor.com/");
        Console.WriteLine();
        Console.WriteLine("Blyant Validator is modelled on these open-source projects, with thanks:");
        Print(Credits.Inspiration);
        Console.WriteLine();
        Console.WriteLine("Built with .NET only: Blyant Validator has no other dependencies.");
        Console.WriteLine("See CREDITS.md for details.");
        return 0;
    }

    private static void Print(IEnumerable<Credits.Entry> entries)
    {
        foreach (var e in entries)
        {
            Console.WriteLine($"  {e.Name} ({e.License})");
            Console.WriteLine($"    {e.Url}");
            Console.WriteLine($"    {e.Role}");
        }
    }
}
