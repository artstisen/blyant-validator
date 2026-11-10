namespace Blyant.Validator.Core;

/// <summary>The kinds of documents Blyant Validator understands.</summary>
public enum DocumentKind
{
    Unknown,
    Html,
    Css,
}

/// <summary>A document to validate or format.</summary>
/// <param name="Path">File path (used for reporting only).</param>
/// <param name="Content">The full text of the document.</param>
/// <param name="Kind">What kind of document it is.</param>
public sealed record SourceDocument(string Path, string Content, DocumentKind Kind)
{
    /// <summary>True when the file started with a UTF-8 byte order mark (kept when it is written back).</summary>
    public bool HasUtf8Bom { get; init; }

    public static DocumentKind DetectKind(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" or ".xhtml" => DocumentKind.Html,
            ".css" => DocumentKind.Css,
            _ => DocumentKind.Unknown,
        };

    /// <summary>The line ending the document mostly uses: CRLF if any line ends with it, otherwise LF.</summary>
    public string DetectNewLine() => Content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}

/// <summary>Checks a document and reports diagnostics.</summary>
public interface IValidator
{
    /// <summary>Short engine name shown in output, e.g. <c>native</c> or <c>vnu</c>.</summary>
    string Engine { get; }

    bool CanValidate(DocumentKind kind);

    Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default);
}

/// <summary>Options shared by all formatters.</summary>
public sealed record FormatOptions
{
    public int IndentSize { get; init; } = 2;
    public bool UseTabs { get; init; }

    /// <summary>Line ending to write. Null keeps the line ending the document already uses.</summary>
    public string? NewLine { get; init; }

    /// <summary>Elements whose content fits within this width are kept on one line (HTML).</summary>
    public int LineWidth { get; init; } = 100;

    public string IndentUnit => UseTabs ? "\t" : new string(' ', IndentSize);
}

/// <summary>Thrown when a document cannot be formatted safely, e.g. because a block is never closed.</summary>
public sealed class FormatException(string message) : Exception(message);

/// <summary>Re-indents and normalises the layout of a document without changing its meaning.</summary>
public interface IFormatter
{
    bool CanFormat(DocumentKind kind);

    /// <exception cref="FormatException">The document has errors that make formatting unsafe.</exception>
    string Format(SourceDocument document, FormatOptions options);
}
