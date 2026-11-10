using System.Reflection;
using Blyant.Validator.Core;

namespace Blyant.Validator.Cli;

internal static class CreditsCommand
{
    public static int Run()
    {
        var version = typeof(CreditsCommand).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        var plus = version.IndexOf('+');
        if (plus >= 0) version = version[..plus];

        Console.WriteLine($"Blyant Validator {version}");
        Console.WriteLine("A companion to the Blyant editor: https://blyant-editor.com/");
        Console.WriteLine();
        Console.WriteLine("Blyant Validator is modelled on these open-source projects, with thanks:");
        Print(Credits.Inspiration);
        Console.WriteLine();
        Console.WriteLine("Built with:");
        Print(Credits.Libraries);
        Console.WriteLine();
        Console.WriteLine("See CREDITS.md and THIRD-PARTY-NOTICES.md for details.");
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
