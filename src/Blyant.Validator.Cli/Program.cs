using Blyant.Validator.Cli;

// Exit codes: 0 = no errors, 1 = errors found (or files need formatting), 2 = the tool itself failed.

var recursive = new OptionSpec("--recursive", "-r",
    "Search directories recursively (folders starting with '.' and node_modules are passed over).");

var validate = new CommandSpec("validate", "Check HTML, CSS, XML (sitemaps, feeds, SVG), Markdown (llms.txt) and Typst files.",
[
    recursive,
    new("--engine", "-e", "Validation engines: 'native' (built in), 'vnu' (the Nu Html Checker for HTML and CSS, needs " +
        "Java), 'typst' (the Typst CLI for Typst files), or both with a comma: 'vnu,typst'. The rest is checked natively.",
        OptionKind.Text, ValueName: "<engines>", Default: "native"),
    new("--output", "-o", "Output format: 'text' or 'json'.",
        OptionKind.Text, Choices: ["text", "json"], Default: "text"),
    new("--vnu-jar", null, "Path to vnu.jar (default: BLYANT_VNU_JAR environment variable).",
        OptionKind.Text, ValueName: "<path>"),
    new("--java", null, "Path to the java executable (default: JAVA_HOME, then PATH).",
        OptionKind.Text, ValueName: "<path>"),
    new("--typst", null, "Path to the typst executable (default: BLYANT_TYPST environment variable, then PATH).",
        OptionKind.Text, ValueName: "<path>"),
    new("--fragment", null, "Check HTML files as fragments: the content of a <body>, for a page made elsewhere (a CMS). " +
        "The doctype, title and language of the page are not checked."),
    new("--ignore", "-i", "Ignore problems with these codes (comma-separated; 'css.*' ignores all codes starting with 'css.'). " +
        "Can be given more than once.", OptionKind.Text, ValueName: "<codes>", Repeatable: true),
], "<paths>", "Files or directories to check.");

var format = new CommandSpec("format", "Re-indent and tidy HTML, CSS and XML files.",
[
    recursive,
    new("--write", "-w", "Overwrite files with the formatted result instead of printing it."),
    new("--check", null, "Don't change anything; exit with code 1 if any file is not formatted."),
    new("--indent-size", null, "Number of spaces per indentation level.",
        OptionKind.Number, ValueName: "<n>", Default: "2", Min: 1, Max: 16),
    new("--use-tabs", null, "Indent with tabs instead of spaces."),
    new("--line-width", null, "HTML elements whose content fits within this width are kept on one line.",
        OptionKind.Number, ValueName: "<n>", Default: "100", Min: 20),
], "<paths>", "HTML, CSS or XML files, or directories, to format.");

var browse = new CommandSpec("browse", "Open the TUI: browse folders and see the problems of each file.",
[
    new("--engine", "-e", "Validation engines: 'native' (built in), 'vnu' (the Nu Html Checker for HTML and CSS, needs " +
        "Java), 'typst' (the Typst CLI for Typst files), or both with a comma: 'vnu,typst'. The rest is checked natively.",
        OptionKind.Text, ValueName: "<engines>", Default: "native"),
    new("--vnu-jar", null, "Path to vnu.jar (default: BLYANT_VNU_JAR environment variable).",
        OptionKind.Text, ValueName: "<path>"),
    new("--java", null, "Path to the java executable (default: JAVA_HOME, then PATH).",
        OptionKind.Text, ValueName: "<path>"),
    new("--typst", null, "Path to the typst executable (default: BLYANT_TYPST environment variable, then PATH).",
        OptionKind.Text, ValueName: "<path>"),
], "<folder>", "The folder to start in (default: where the TUI was left last).", ArgumentRequired: false);

var credits = new CommandSpec("credits", "Show the open-source projects Blyant Validator builds on.", []);

CommandSpec[] commands = [validate, format, browse, credits];

// Results are written as UTF-8 (messages have “quotes”, and format prints the files), also when they go to a file or a
// pipe: the code page of a Windows console would turn other characters into "?"
if (args.Length > 0)
{
    try
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
    }
    catch (Exception e) when (e is IOException or PlatformNotSupportedException)
    {
        // The console keeps its own
    }
}

if (args.Length == 0)
{
    // In a terminal: the TUI, where it was left last. Otherwise (output redirected, a script): the help, as a mistake
    if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
        return BrowseCommand.Run(null, Engines.Native, null, null, null);
    Console.Error.Write(CommandLine.RootHelp(commands));
    return 2;
}
if (args[0] is "-h" or "--help" or "-?" or "/?")
{
    Console.Out.Write(CommandLine.RootHelp(commands));
    return 0;
}
if (args[0] == "--version")
{
    Console.Out.WriteLine(CommandLine.Version);
    return 0;
}

var command = commands.FirstOrDefault(c => c.Name == args[0]);
if (command is null)
{
    Console.Error.WriteLine($"{CommandLine.Program}: unknown command '{args[0]}'.");
    Console.Error.WriteLine($"Run '{CommandLine.Program} --help' to see the commands.");
    return 2;
}

ParsedArguments parsed;
try
{
    parsed = CommandLine.Parse(command, args[1..]);
}
catch (UsageException ex)
{
    Console.Error.WriteLine($"{CommandLine.Program}: {ex.Message}");
    Console.Error.WriteLine($"Run '{CommandLine.Program} {command.Name} --help' to see its options.");
    return 2;
}

if (parsed.Help)
{
    Console.Out.Write(CommandLine.CommandHelp(command));
    return 0;
}

if (command.Name == "format" && parsed.Flag("--check") && parsed.Flag("--write"))
{
    Console.Error.WriteLine($"{CommandLine.Program}: use --check or --write, not both.");
    Console.Error.WriteLine($"Run '{CommandLine.Program} format --help' to see its options.");
    return 2;
}

// The engines (validate, browse)
var engines = Engines.Native;
if (command.Name is "validate" or "browse")
{
    if (Engines.Parse(parsed.Text("--engine")!) is not { } chosen)
    {
        Console.Error.WriteLine($"{CommandLine.Program}: '{parsed.Text("--engine")}' is not an engine: use native, vnu, typst or vnu,typst.");
        Console.Error.WriteLine($"Run '{CommandLine.Program} {command.Name} --help' to see its options.");
        return 2;
    }

    engines = chosen;
}

// The codes to ignore (validate)
List<string> ignore = [];
foreach (var entry in command.Name == "validate" ? parsed.Texts("--ignore").SelectMany(Blyant.Validator.Core.IgnoreList.Split) : [])
{
    if (!Blyant.Validator.Core.IgnoreList.IsValid(entry))
    {
        Console.Error.WriteLine($"{CommandLine.Program}: '{entry}' is not a diagnostic code to ignore (as 'html.missing-lang' or 'css.*').");
        Console.Error.WriteLine($"Run '{CommandLine.Program} {command.Name} --help' to see its options.");
        return 2;
    }

    if (!ignore.Contains(entry)) ignore.Add(entry);
}

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

try
{
    return command.Name switch
    {
        "validate" => await ValidateCommand.RunAsync(new ValidateCommand.Settings(
            [.. parsed.Arguments],
            parsed.Flag("--recursive"),
            engines,
            parsed.Text("--output")!,
            parsed.Text("--vnu-jar"),
            parsed.Text("--java"),
            parsed.Flag("--fragment"),
            ignore,
            parsed.Text("--typst")), cancel.Token),
        "format" => await FormatCommand.RunAsync(new FormatCommand.Settings(
            [.. parsed.Arguments],
            parsed.Flag("--recursive"),
            parsed.Flag("--write"),
            parsed.Flag("--check"),
            parsed.Number("--indent-size"),
            parsed.Flag("--use-tabs"),
            parsed.Number("--line-width")), cancel.Token),
        "browse" => BrowseCommand.Run(
            parsed.Arguments.FirstOrDefault(),
            engines,
            parsed.Text("--vnu-jar"),
            parsed.Text("--java"),
            parsed.Text("--typst")),
        _ => CreditsCommand.Run(),
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"{CommandLine.Program}: stopped.");
    return 2;
}
catch (Exception e) when (e is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"{CommandLine.Program}: {e.Message}");
    return 2;
}
catch (Exception e)
{
    // A mistake of the program itself: it still ends with exit code 2 (the tool failed), with what is needed to find it
    Console.Error.WriteLine($"{CommandLine.Program}: internal error: {e}");
    return 2;
}
