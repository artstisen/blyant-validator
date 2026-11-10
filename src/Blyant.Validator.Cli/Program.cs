using Blyant.Validator.Cli;

// Exit codes: 0 = no errors, 1 = errors found (or files need formatting), 2 = the tool itself failed.

var recursive = new OptionSpec("--recursive", "-r", "Search directories recursively for .html, .htm and .css files.");

var validate = new CommandSpec("validate", "Check HTML and CSS files for errors.",
[
    recursive,
    new("--engine", "-e", "Validation engine: 'native' (built in) or 'vnu' (Nu Html Checker, needs Java).",
        OptionKind.Text, Choices: ["native", "vnu"], Default: "native"),
    new("--output", "-o", "Output format: 'text' or 'json'.",
        OptionKind.Text, Choices: ["text", "json"], Default: "text"),
    new("--vnu-jar", null, "Path to vnu.jar (default: BLYANT_VNU_JAR environment variable).",
        OptionKind.Text, ValueName: "<path>"),
    new("--java", null, "Path to the java executable (default: JAVA_HOME, then PATH).",
        OptionKind.Text, ValueName: "<path>"),
], "<paths>", "HTML/CSS files or directories to process.");

var format = new CommandSpec("format", "Re-indent and tidy HTML and CSS files.",
[
    recursive,
    new("--write", "-w", "Overwrite files with the formatted result instead of printing it."),
    new("--check", null, "Don't change anything; exit with code 1 if any file is not formatted."),
    new("--indent-size", null, "Number of spaces per indentation level.",
        OptionKind.Number, ValueName: "<n>", Default: "2", Min: 1, Max: 16),
    new("--use-tabs", null, "Indent with tabs instead of spaces."),
    new("--line-width", null, "HTML elements whose content fits within this width are kept on one line.",
        OptionKind.Number, ValueName: "<n>", Default: "100", Min: 20),
], "<paths>", "HTML/CSS files or directories to process.");

var browse = new CommandSpec("browse", "Open the TUI: browse folders and see the problems of each file.",
[
    new("--engine", "-e", "Validation engine: 'native' (built in) or 'vnu' (Nu Html Checker, needs Java).",
        OptionKind.Text, Choices: ["native", "vnu"], Default: "native"),
    new("--vnu-jar", null, "Path to vnu.jar (default: BLYANT_VNU_JAR environment variable).",
        OptionKind.Text, ValueName: "<path>"),
    new("--java", null, "Path to the java executable (default: JAVA_HOME, then PATH).",
        OptionKind.Text, ValueName: "<path>"),
], "<folder>", "The folder to start in (default: where the TUI was left last).", ArgumentRequired: false);

var credits = new CommandSpec("credits", "Show the open-source projects Blyant Validator builds on.", []);

CommandSpec[] commands = [validate, format, browse, credits];

if (args.Length == 0)
{
    // In a terminal: the TUI, where it was left last. Otherwise (output redirected, a script): the help, as a mistake
    if (!Console.IsInputRedirected && !Console.IsOutputRedirected)
        return BrowseCommand.Run(null, "native", null, null);
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
            parsed.Text("--engine")!,
            parsed.Text("--output")!,
            parsed.Text("--vnu-jar"),
            parsed.Text("--java")), cancel.Token),
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
            parsed.Text("--engine")!,
            parsed.Text("--vnu-jar"),
            parsed.Text("--java")),
        _ => CreditsCommand.Run(),
    };
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine($"{CommandLine.Program}: stopped.");
    return 2;
}
