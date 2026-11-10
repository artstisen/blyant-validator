using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Dom.Events;
using AngleSharp.Html.Parser;
using AngleSharp.Text;
using Blyant.Validator.Core;
using Blyant.Validator.Html.Rules;

namespace Blyant.Validator.Html;

/// <summary>
/// Native HTML validator. Parses with AngleSharp (which reports WHATWG parse errors) and then runs
/// conformance rules over the resulting DOM.
/// </summary>
public sealed class HtmlValidator : IValidator
{
    private readonly IReadOnlyList<IHtmlRule> _rules;

    public HtmlValidator() : this(HtmlRuleSet.Default) { }

    public HtmlValidator(IReadOnlyList<IHtmlRule> rules) => _rules = rules;

    public string Engine => "native";

    public bool CanValidate(DocumentKind kind) => kind == DocumentKind.Html;

    public Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<Diagnostic>();

        var parser = new HtmlParser(new HtmlParserOptions
        {
            IsKeepingSourceReferences = true,
        });

        parser.Error += (_, ev) =>
        {
            if (ev is HtmlErrorEvent error)
            {
                var pos = ToPosition(error.Position);
                diagnostics.Add(new Diagnostic(
                    document.Path, Severity.Error, $"html.parse.{error.Code}", error.Message, pos, pos, Engine));
            }
        };

        var dom = parser.ParseDocument(document.Content);

        var context = new HtmlRuleContext(document, dom, diagnostics);
        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rule.Check(context);
        }

        IReadOnlyList<Diagnostic> sorted = [.. diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column)];
        return Task.FromResult(sorted);
    }

    internal static SourcePosition ToPosition(TextPosition position) =>
        position.Line > 0 ? new SourcePosition(position.Line, position.Column) : SourcePosition.Unknown;

    internal static SourcePosition PositionOf(INode node) =>
        node is IElement { SourceReference: { } source } ? ToPosition(source.Position) : SourcePosition.Unknown;
}

/// <summary>State passed to each <see cref="IHtmlRule"/>.</summary>
public sealed class HtmlRuleContext(SourceDocument document, IHtmlDocument dom, List<Diagnostic> sink)
{
    public SourceDocument Document { get; } = document;
    public IHtmlDocument Dom { get; } = dom;

    public void Report(INode node, Severity severity, string code, string message)
    {
        var pos = HtmlValidator.PositionOf(node);
        sink.Add(new Diagnostic(Document.Path, severity, code, message, pos, pos));
    }
}
