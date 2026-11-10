using Blyant.Validator.Core;

namespace Blyant.Validator.Css;

/// <summary>
/// Native CSS validator. Not implemented yet: the plan is a CSS Syntax Level 3 tokenizer and parser,
/// with property values checked against grammar data from w3c/webref.
/// </summary>
public sealed class CssValidator : IValidator
{
    public string Engine => "native";

    public bool CanValidate(DocumentKind kind) => kind == DocumentKind.Css;

    public Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("CSS validation is not implemented yet.");
}

/// <summary>
/// CSS formatter. Not implemented yet: the plan is a token-stream pretty-printer that puts one
/// declaration per line and preserves comments.
/// </summary>
public sealed class CssFormatter : IFormatter
{
    public bool CanFormat(DocumentKind kind) => kind == DocumentKind.Css;

    public string Format(SourceDocument document, FormatOptions options) =>
        throw new NotImplementedException("CSS formatting is not implemented yet.");
}
