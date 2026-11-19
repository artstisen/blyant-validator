using Blyant.Validator.Cli.Tui.Terminal;
using Blyant.Validator.Core;

namespace Blyant.Validator.Cli.Tui;

// ":about": the version of Blyant Validator, its copyright and licence, and whom it builds on
internal static partial class Browser
{
    /// <summary>The text of the about box, with the colors of its lines (an empty line is a blank row)</summary>
    internal static IReadOnlyList<(string Text, ConsoleColors.ColorPreset Colors)> AboutTexts() =>
    [
        ($"{Menu.ApplicationName} {CommandLine.Version}", ConsoleColors.ColorPreset.HeaderColor),
        ("Validates and formats HTML, CSS and XML files, and validates Markdown and Typst, in the terminal. " +
         "A companion to the Blyant editor (https://blyant-editor.com/).", ConsoleColors.ColorPreset.Normal),
        (string.Empty, ConsoleColors.ColorPreset.Normal),
        (CommandLine.Copyright, ConsoleColors.ColorPreset.Normal),
        ("Released under the MIT License: see the file LICENSE next to the program.", ConsoleColors.ColorPreset.Normal),
        (string.Empty, ConsoleColors.ColorPreset.Normal),
        ($"Modelled on {string.Join(" and ", Credits.Inspiration.Select(c => InSentence(c.Name)))}, and written from " +
         "scratch. The file browser is adapted from Blyant's.", ConsoleColors.ColorPreset.Divider),
        ($"Run “{CommandLine.Program} credits” for more.", ConsoleColors.ColorPreset.Divider),
    ];

    // A name of the credits in a sentence: "The Nu Html Checker" as "the Nu Html Checker", "W3C …" as "the W3C …"
    private static string InSentence(string name) => name.StartsWith("The ", StringComparison.Ordinal) ? "the " + name[4..] : "the " + name;

    private static void ShowAbout() => ChoiceBox.ShowTexts("About", AboutTexts());
}
