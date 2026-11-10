using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;
using Blyant.Validator.Html.Rules;

namespace Blyant.Validator.Html;

/// <summary>
/// Native HTML validator. Reads the document with <see cref="HtmlReader"/> (which reports parse errors)
/// and then runs conformance rules over the tree.
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
        var lines = new LineMap(document.Content);
        var result = HtmlReader.Read(document.Content);

        foreach (var error in result.Errors)
        {
            var pos = lines[error.Offset];
            diagnostics.Add(new Diagnostic(document.Path, Severity.Error, error.Code, error.Message, pos, pos, Engine));
        }

        var context = new HtmlRuleContext(document, result.Root, lines, diagnostics);
        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rule.Check(context);
        }

        IReadOnlyList<Diagnostic> sorted = [.. diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column)];
        return Task.FromResult(sorted);
    }
}

/// <summary>State passed to each <see cref="IHtmlRule"/>.</summary>
public sealed class HtmlRuleContext(SourceDocument document, HElement root, LineMap lines, List<Diagnostic> sink)
{
    public SourceDocument Document { get; } = document;

    /// <summary>The document; its children are the top-level nodes.</summary>
    public HElement Root { get; } = root;

    /// <summary>The first element with the given name outside SVG and MathML, if any.</summary>
    public HElement? Find(string name) =>
        Root.Descendants().FirstOrDefault(e => e.Lower == name && !e.IsForeign);

    public IEnumerable<HElement> FindAll(string name) =>
        Root.Descendants().Where(e => e.Lower == name && !e.IsForeign);

    public void Report(HNode? node, Severity severity, string code, string message)
    {
        var pos = lines[node?.Start ?? 0];
        sink.Add(new Diagnostic(Document.Path, severity, code, message, pos, pos));
    }
}
