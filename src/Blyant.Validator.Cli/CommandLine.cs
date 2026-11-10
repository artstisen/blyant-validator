using System.Globalization;
using System.Reflection;
using System.Text;

namespace Blyant.Validator.Cli;

internal enum OptionKind
{
    Flag,
    Text,
    Number,
}

/// <summary>An option of a command, e.g. <c>-e, --engine &lt;native|vnu&gt;</c>.</summary>
internal sealed record OptionSpec(
    string Name,
    string? Alias,
    string Description,
    OptionKind Kind = OptionKind.Flag,
    string? ValueName = null,
    string[]? Choices = null,
    string? Default = null,
    int Min = 0,
    int Max = int.MaxValue);

/// <summary>A command with its options and its (one or more) path arguments.</summary>
internal sealed record CommandSpec(
    string Name,
    string Description,
    IReadOnlyList<OptionSpec> Options,
    string? ArgumentName = null,
    string? ArgumentDescription = null,
    bool ArgumentRequired = true);

/// <summary>A mistake in the command line. The message is shown with a hint to use --help.</summary>
internal sealed class UsageException(string message) : Exception(message);

/// <summary>The options and arguments given to a command.</summary>
internal sealed class ParsedArguments(CommandSpec command)
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public List<string> Arguments { get; } = [];

    public bool Help { get; set; }

    public void Set(OptionSpec option, string value) => _values[option.Name] = value;

    private OptionSpec Spec(string name) => command.Options.Single(o => o.Name == name);

    public bool Flag(string name) => _values.ContainsKey(name);

    public string? Text(string name) => _values.TryGetValue(name, out var v) ? v : Spec(name).Default;

    public int Number(string name) =>
        int.Parse(_values.TryGetValue(name, out var v) ? v : Spec(name).Default!, CultureInfo.InvariantCulture);
}

/// <summary>
/// A small command-line parser: <c>--name value</c>, <c>--name=value</c>, <c>-x value</c>, flags,
/// <c>--</c> to end the options, and help texts. Blyant Validator has no dependencies besides .NET.
/// </summary>
internal static class CommandLine
{
    public const string Program = "blyant-validator";

    public static readonly OptionSpec HelpOption = new("--help", "-h", "Show help and usage information.");

    public static string Version
    {
        get
        {
            var version = typeof(CommandLine).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }

    public static ParsedArguments Parse(CommandSpec command, IReadOnlyList<string> args)
    {
        var result = new ParsedArguments(command);
        var onlyArguments = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (onlyArguments || arg == "-" || !arg.StartsWith('-'))
            {
                result.Arguments.Add(arg);
                continue;
            }
            if (arg == "--")
            {
                onlyArguments = true;
                continue;
            }
            if (arg is "-h" or "--help" or "-?" or "/?")
            {
                result.Help = true;
                continue;
            }

            var eq = arg.IndexOf('=');
            var name = eq >= 0 ? arg[..eq] : arg;
            var option = command.Options.FirstOrDefault(o => o.Name == name || o.Alias == name)
                ?? throw new UsageException($"Unknown option '{name}' for '{command.Name}'.");

            if (option.Kind == OptionKind.Flag)
            {
                if (eq >= 0) throw new UsageException($"Option '{option.Name}' does not take a value.");
                result.Set(option, "true");
                continue;
            }

            string value;
            if (eq >= 0) value = arg[(eq + 1)..];
            else if (i + 1 < args.Count) value = args[++i];
            else throw new UsageException($"Option '{option.Name}' needs a value: {option.ValueName ?? "<value>"}.");

            if (option.Choices is { } choices)
            {
                var match = choices.FirstOrDefault(c => c.Equals(value, StringComparison.OrdinalIgnoreCase))
                    ?? throw new UsageException($"'{value}' is not a valid value for '{option.Name}'. Use one of: {string.Join(", ", choices)}.");
                value = match;
            }
            if (option.Kind == OptionKind.Number &&
                (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < option.Min || n > option.Max))
            {
                throw new UsageException(option.Max == int.MaxValue
                    ? $"'{value}' is not a valid value for '{option.Name}': use a whole number of at least {option.Min}."
                    : $"'{value}' is not a valid value for '{option.Name}': use a whole number from {option.Min} to {option.Max}.");
            }

            result.Set(option, value);
        }

        if (!result.Help && command.ArgumentName is not null && command.ArgumentRequired && result.Arguments.Count == 0)
            throw new UsageException($"'{command.Name}' needs at least one {command.ArgumentName.Trim('<', '>')} argument.");

        return result;
    }

    public static string CommandHelp(CommandSpec command)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Description:");
        sb.AppendLine("  " + command.Description);
        sb.AppendLine();
        sb.AppendLine("Usage:");
        sb.Append($"  {Program} {command.Name}");
        if (command.ArgumentName is not null)
            sb.Append(command.ArgumentRequired ? $" {command.ArgumentName}..." : $" [{command.ArgumentName}]");
        sb.AppendLine(" [options]");

        if (command.ArgumentName is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Arguments:");
            AppendTable(sb, [(command.ArgumentName, command.ArgumentDescription ?? "")]);
        }

        sb.AppendLine();
        sb.AppendLine("Options:");
        var rows = command.Options.Append(HelpOption).Select(o =>
        {
            var left = (o.Alias is null ? "" : o.Alias + ", ") + o.Name;
            if (o.Kind != OptionKind.Flag)
                left += " " + (o.Choices is null ? o.ValueName ?? "<value>" : $"<{string.Join('|', o.Choices)}>");
            var right = o.Description + (o.Default is null ? "" : $" [default: {o.Default}]");
            return (left, right);
        });
        AppendTable(sb, rows);
        return sb.ToString();
    }

    public static string RootHelp(IReadOnlyList<CommandSpec> commands)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Blyant Validator {Version}: validate and format HTML and CSS files, and validate XML.");
        sb.AppendLine();
        sb.AppendLine("Usage:");
        sb.AppendLine($"  {Program} <command> [options]");
        sb.AppendLine($"  {Program}            (no command: the TUI, where it was left last)");
        sb.AppendLine();
        sb.AppendLine("Commands:");
        AppendTable(sb, commands.Select(c => (c.Name, c.Description)));
        sb.AppendLine();
        sb.AppendLine("Options:");
        AppendTable(sb, [("-h, --help", "Show help and usage information."), ("--version", "Show the version.")]);
        sb.AppendLine();
        sb.AppendLine($"Run '{Program} <command> --help' to see the options of a command.");
        return sb.ToString();
    }

    private static void AppendTable(StringBuilder sb, IEnumerable<(string Left, string Right)> rows)
    {
        var list = rows.ToList();
        var width = list.Max(r => r.Left.Length) + 2;
        foreach (var (left, right) in list)
            sb.AppendLine("  " + left.PadRight(width) + right);
    }
}
