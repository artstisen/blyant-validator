using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Blyant.Validator.Core;

namespace Blyant.Validator.Xml;

/// <summary>
/// Native XML validator: well-formedness (XML 1.0), with .NET's own XML reader, and the rules of the sitemap protocol
/// (https://www.sitemaps.org/protocol.html) for sitemaps. DTDs are ignored and nothing outside the file is loaded, so
/// a file cannot make the validator read other files or the network (XML external entities).
/// </summary>
public sealed partial class XmlValidator : IValidator
{
    public string Engine => "native";

    public bool CanValidate(DocumentKind kind) => kind == DocumentKind.Xml;

    public Task<IReadOnlyList<Diagnostic>> ValidateAsync(SourceDocument document, CancellationToken cancellationToken = default) =>
        Task.FromResult(Validate(document.Path, document.Content));

    /// <summary>Validates XML given as text.</summary>
    public static IReadOnlyList<Diagnostic> Validate(string path, string xml)
    {
        var diagnostics = new List<Diagnostic>();
        var document = Parse(path, xml, diagnostics);
        if (document?.Root is { } root)
        {
            if (Sitemap.IsSitemap(root)) Sitemap.Check(path, xml, root, diagnostics);
            else if (Feeds.IsRss(root)) Feeds.CheckRss(path, root, diagnostics);
            else if (Feeds.IsAtom(root)) Feeds.CheckAtom(path, root, diagnostics);
        }
        return [.. diagnostics.OrderBy(d => d.Start.Line).ThenBy(d => d.Start.Column)];
    }

    /// <summary>
    /// Reads the XML, keeping the line and column of every element. A file that is not well-formed gives one error, at
    /// the first place that is wrong (XML stops there, as browsers do), and null
    /// </summary>
    private static XDocument? Parse(string path, string xml, List<Diagnostic> diagnostics)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreWhitespace = false,
        };

        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            return XDocument.Load(reader, LoadOptions.SetLineInfo);
        }
        catch (XmlException e)
        {
            var at = new SourcePosition(Math.Max(1, e.LineNumber), Math.Max(1, e.LinePosition));
            diagnostics.Add(new Diagnostic(path, Severity.Error, "xml.not-well-formed", Explain(e.Message), at, at));
            return null;
        }
    }

    // .NET's message without its own " Line 3, position 7." (the place is told by the diagnostic), and the most
    // common mistakes told in plain words
    private static string Explain(string message)
    {
        message = LinePosition().Replace(message, string.Empty).Trim();
        if (message.Contains("parsing EntityName", StringComparison.Ordinal))
            return "“&” does not start a reference here: write “&amp;” for an ampersand.";
        if (UndeclaredEntity().Match(message) is { Success: true } entity)
            return $"Reference to undeclared entity “&{entity.Groups[1].Value};”. XML only knows &amp; &lt; &gt; &quot; and &apos;: " +
                   "use a number instead (as &#160; for a no-break space), or the character itself.";
        return message;
    }

    [GeneratedRegex(@"undeclared entity '([^']+)'")]
    private static partial Regex UndeclaredEntity();

    [GeneratedRegex(@"\s*Line \d+, position \d+\.?")]
    private static partial Regex LinePosition();

    /// <summary>The place of an element or attribute (1:1 when it is not known).</summary>
    internal static SourcePosition PositionOf(XObject node) =>
        node is IXmlLineInfo { } info && info.HasLineInfo()
            ? new SourcePosition(info.LineNumber, info.LinePosition)
            : new SourcePosition(1, 1);
}
