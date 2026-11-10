using System.CommandLine;
using Blyant.Validator.Cli;

// Exit codes: 0 = no errors, 1 = errors found (or files need formatting), 2 = the tool itself failed.

var pathsArgument = new Argument<string[]>("paths")
{
    Description = "HTML/CSS files or directories to process.",
    Arity = ArgumentArity.OneOrMore,
};

var recursiveOption = new Option<bool>("--recursive", "-r")
{
    Description = "Search directories recursively for .html, .htm and .css files.",
};

// ---- validate ---------------------------------------------------------------------------------

var engineOption = new Option<string>("--engine", "-e")
{
    Description = "Validation engine: 'native' (built in) or 'vnu' (Nu Html Checker, needs Java).",
    DefaultValueFactory = _ => "native",
};
engineOption.AcceptOnlyFromAmong("native", "vnu");

var outputOption = new Option<string>("--output", "-o")
{
    Description = "Output format: 'text' or 'json'.",
    DefaultValueFactory = _ => "text",
};
outputOption.AcceptOnlyFromAmong("text", "json");

var vnuJarOption = new Option<string?>("--vnu-jar")
{
    Description = "Path to vnu.jar (default: BLYANT_VNU_JAR environment variable).",
};

var javaOption = new Option<string?>("--java")
{
    Description = "Path to the java executable (default: JAVA_HOME, then PATH).",
};

var validateCommand = new Command("validate", "Check HTML and CSS files for errors.")
{
    pathsArgument, recursiveOption, engineOption, outputOption, vnuJarOption, javaOption,
};
validateCommand.SetAction((parseResult, ct) => ValidateCommand.RunAsync(
    new ValidateCommand.Settings(
        parseResult.GetValue(pathsArgument)!,
        parseResult.GetValue(recursiveOption),
        parseResult.GetValue(engineOption)!,
        parseResult.GetValue(outputOption)!,
        parseResult.GetValue(vnuJarOption),
        parseResult.GetValue(javaOption)),
    ct));

// ---- format -----------------------------------------------------------------------------------

var writeOption = new Option<bool>("--write", "-w")
{
    Description = "Overwrite files with the formatted result instead of printing it.",
};

var checkOption = new Option<bool>("--check")
{
    Description = "Don't change anything; exit with code 1 if any file is not formatted.",
};

var indentSizeOption = new Option<int>("--indent-size")
{
    Description = "Number of spaces per indentation level.",
    DefaultValueFactory = _ => 2,
};

var useTabsOption = new Option<bool>("--use-tabs")
{
    Description = "Indent with tabs instead of spaces.",
};

var formatCommand = new Command("format", "Re-indent and tidy HTML and CSS files.")
{
    pathsArgument, recursiveOption, writeOption, checkOption, indentSizeOption, useTabsOption,
};
formatCommand.SetAction((parseResult, ct) => FormatCommand.RunAsync(
    new FormatCommand.Settings(
        parseResult.GetValue(pathsArgument)!,
        parseResult.GetValue(recursiveOption),
        parseResult.GetValue(writeOption),
        parseResult.GetValue(checkOption),
        parseResult.GetValue(indentSizeOption),
        parseResult.GetValue(useTabsOption)),
    ct));

// ---- credits ----------------------------------------------------------------------------------

var creditsCommand = new Command("credits", "Show the open-source projects Blyant Validator builds on.");
creditsCommand.SetAction(_ => CreditsCommand.Run());

// ---- root -------------------------------------------------------------------------------------

var root = new RootCommand("Blyant Validator: validate and format HTML and CSS files.")
{
    validateCommand, formatCommand, creditsCommand,
};

return await root.Parse(args).InvokeAsync();
