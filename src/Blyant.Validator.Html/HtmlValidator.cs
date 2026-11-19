using Blyant.Validator.Core;
using Blyant.Validator.Html.Parsing;
using Blyant.Validator.Html.Rules;

namespace Blyant.Validator.Html;

/// <summary>What the native HTML checks are asked to do.</summary>
/// <param name="Fragment">Check the file as a fragment: the content of a &lt;body&gt; that is inserted in a page made
/// elsewhere (an article for a CMS). The checks of the page around it (the doctype, the title, the language, the
/// character encoding, links within the page and to files) are left out, and the parts of a page that a fragment
/// should not have are warned about.</param>
/// <param name="Accessibility">Also give the accessibility hints (<see cref="HtmlRuleSet.Accessibility"/>), as info:
/// links and buttons without text, form fields without a label, heading levels that skip …</param>
/// <param name="Exists">Whether a file or folder is there, for relative links and images (the program gives
/// DocumentIo.IsFileOrFolder); null: they are not checked. The checks themselves never touch the file system.</param>
public sealed record HtmlCheckOptions(bool Fragment = false, bool Accessibility = false, Func<string, bool>? Exists = null);

/// <summary>
/// Native HTML validator. Reads the document with <see cref="HtmlReader"/> (which reports parse errors)
/// and then runs conformance rules over the tree.
/// </summary>
public sealed class HtmlValidator : IValidator
{
    private readonly IReadOnlyList<IHtmlRule> _rules;
    private readonly HtmlCheckOptions _options;

    public HtmlValidator() : this(new HtmlCheckOptions()) { }

    /// <param name="fragment">Check the file as a fragment (see <see cref="HtmlCheckOptions.Fragment"/>).</param>
    public HtmlValidator(bool fragment) : this(new HtmlCheckOptions(fragment)) { }

    public HtmlValidator(HtmlCheckOptions options)
    {
        _options = options;
        _rules = [.. options.Fragment ? HtmlRuleSet.Fragment : HtmlRuleSet.Default, .. options.Accessibility ? HtmlRuleSet.Accessibility : []];
    }

    public HtmlValidator(IReadOnlyList<IHtmlRule> rules)
    {
        _rules = rules;
        _options = new HtmlCheckOptions();
    }

    public string Engine => "native";

    public bool CanValidate(DocumentKind kind) => kind == DocumentKind.Html;

    public Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<Diagnostic>();
        var lines = new LineMap(document.Content);
        var result = HtmlReader.Read(document.Content);

        foreach (var error in result.Errors)
        {
            if (_options.Fragment && error.Code is "html.parse.missing-doctype" or "html.parse.content-after-body") continue;
            var pos = lines[error.Offset];
            diagnostics.Add(new Diagnostic(document.Path, Severity.Error, error.Code, error.Message, pos, pos, Engine));
        }

        if (result.TooDeepAt >= 0)
        {
            // The rules walk the tree recursively: a tree this deep is not checked (it would overflow the stack)
            var at = lines[result.TooDeepAt];
            diagnostics.Add(new Diagnostic(document.Path, Severity.Error, "html.too-deep",
                $"Elements are nested more than {HtmlReader.MaxDepth} deep (are end tags missing?): the rest of the checks are left out.",
                at, at, Engine));
            return Task.FromResult<IReadOnlyList<Diagnostic>>([.. diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column)]);
        }

        var context = new HtmlRuleContext(document, result.Root, lines, diagnostics) { Options = _options };
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

    /// <summary>What the checks are asked to do (a fragment, the accessibility hints, the files around the file).</summary>
    public HtmlCheckOptions Options { get; init; } = new();

    /// <summary>The document; its children are the top-level nodes.</summary>
    public HElement Root { get; } = root;

    /// <summary>The first element with the given name outside SVG and MathML, if any.</summary>
    public HElement? Find(string name) =>
        Root.Descendants().FirstOrDefault(e => e.Lower == name && !e.IsForeign);

    public IEnumerable<HElement> FindAll(string name) =>
        Root.Descendants().Where(e => e.Lower == name && !e.IsForeign);

    public void Report(HNode? node, Severity severity, string code, string message) =>
        Report(node?.Start ?? 0, severity, code, message);

    /// <summary>Report a problem at an offset of the file (an attribute, a value).</summary>
    public void Report(int offset, Severity severity, string code, string message)
    {
        var pos = lines[offset];
        sink.Add(new Diagnostic(Document.Path, severity, code, message, pos, pos));
    }

    /// <summary>The line and column of an offset of the file.</summary>
    public SourcePosition PositionOf(int offset) => lines[offset];

    /// <summary>Add a problem found by another validator (the CSS of the file).</summary>
    public void Add(Diagnostic diagnostic) => sink.Add(diagnostic);
}
