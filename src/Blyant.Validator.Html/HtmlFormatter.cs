using Blyant.Validator.Core;

namespace Blyant.Validator.Html;

/// <summary>
/// HTML formatter. Not implemented yet: the plan is a token-stream pretty-printer that re-indents
/// block-level elements and leaves &lt;pre&gt;, &lt;textarea&gt;, &lt;script&gt;, &lt;style&gt; and comments untouched.
/// </summary>
public sealed class HtmlFormatter : IFormatter
{
    public bool CanFormat(DocumentKind kind) => kind == DocumentKind.Html;

    public string Format(SourceDocument document, FormatOptions options) =>
        throw new NotImplementedException("HTML formatting is not implemented yet.");
}
