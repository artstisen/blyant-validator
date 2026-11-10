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
    public static DocumentKind DetectKind(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" or ".xhtml" => DocumentKind.Html,
            ".css" => DocumentKind.Css,
            _ => DocumentKind.Unknown,
        };

    public static async Task<SourceDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return new SourceDocument(path, content, DetectKind(path));
    }
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
    public string NewLine { get; init; } = "\n";

    public string IndentUnit => UseTabs ? "\t" : new string(' ', IndentSize);
}

/// <summary>Re-indents and normalises the layout of a document without changing its meaning.</summary>
public interface IFormatter
{
    bool CanFormat(DocumentKind kind);

    string Format(SourceDocument document, FormatOptions options);
}
