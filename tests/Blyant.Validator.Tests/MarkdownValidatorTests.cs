using Blyant.Validator.Core;
using Blyant.Validator.Markdown;

namespace Blyant.Validator.Tests;

public class MarkdownValidatorTests
{
    private static IReadOnlyList<Diagnostic> Validate(string text, string name = "README.md", Func<string, bool>? exists = null) =>
        MarkdownValidator.Validate(name, text.ReplaceLineEndings("\n"), exists);

    [Fact]
    public void RealisticMarkdown_HasNoProblems()
    {
        const string text = """
            ---
            title: Guide
            tags: [a, b]
            ---
            # Guide to the editor

            Text with `inline [code][x]` and a [link](https://example.org/a_(b)) and an ![image](pic.png "Title").
            See [the keys](#keys-and-commands), [modes](#modes-1), the [manual][manual] and [the FAQ][].
            A shortcut [label] that is not defined is just text. Escaped \[not a link\](x).

            ## Keys and commands

            1. First item
               continued on an indented line
                   - nested, deeper
            2. Second

            - item with a code block:

                  not [code][y] checked

            ```markdown
            # Not a heading
            [broken](
            ```

            ~~~
            [also][ignored]
            ~~~

                indented code [z][w]

            Modes
            -----

            ## Modes

            <a id="custom-anchor"></a>
            Jump to [custom](#custom-anchor). Mail [me](mailto:someone) or call [us](tel:123).

            > A quote with a [link](https://example.org).

            ***

            [manual]: https://example.org/manual
            [the faq]: <https://example.org/faq> "FAQ"
            """;

        Assert.Empty(Validate(text, exists: _ => true));
    }

    [Theory]
    [InlineData("# A\n\n```\ncode", "md.unclosed-fence")]
    [InlineData("#Heading", "md.heading-space")]
    [InlineData("# A\n\n#", "md.empty-heading")]
    [InlineData("# A\n\n# B", "md.multiple-h1")]
    [InlineData("# A\n\n### C", "md.heading-skip")]
    [InlineData("Go [there](", "md.unclosed-link")]
    [InlineData("A [link]() here", "md.empty-link")]
    [InlineData("See [x][nothing]", "md.undefined-reference")]
    [InlineData("[a]: one\n[A]: two", "md.duplicate-reference")]
    [InlineData("# A\n\nSee [b](#b)", "md.broken-anchor")]
    public void Problem_IsAWarning(string text, string code)
    {
        var d = Assert.Single(Validate(text), d => d.Code == code);
        Assert.Equal(Severity.Warning, d.Severity);
    }

    [Fact]
    public void RelativeLinks_AreCheckedWithTheGivenFunction()
    {
        var root = Path.GetFullPath(".");
        var asked = new List<string>();
        bool Exists(string path)
        {
            asked.Add(path);
            return path.EndsWith("there.md", StringComparison.Ordinal);
        }

        var result = Validate("[a](docs/there.md#x) [b](docs/gone.md) [c](https://x.org/gone.md) [d](/root.md) ![e](img%20name.png) [f](docs\\back.md)",
            Path.Combine(root, "README.md"), Exists);

        Assert.Equal(3, result.Count(d => d.Code == "md.broken-link"));
        Assert.Contains(Path.Combine(root, "docs", "gone.md"), asked);
        Assert.Contains(Path.Combine(root, "img name.png"), asked);
        // (a link written with "\" is the same path on every system)
        Assert.Contains(Path.Combine(root, "docs", "back.md"), asked);
        Assert.DoesNotContain(asked, p => p.Contains("x.org") || p.EndsWith("root.md"));
    }

    [Fact]
    public void RelativeLinks_AreNotChecked_WithoutAFunction()
    {
        Assert.Empty(Validate("[a](missing.md)"));
    }

    [Fact]
    public void Problem_PointsAtTheLink()
    {
        var d = Assert.Single(Validate("# A\n\nText and [x][nope] here"));
        Assert.Equal(new SourcePosition(3, 10), d.Start);
    }

    private const string Llms = """
        # Blyant

        > Blyant is a free, open-source, Vim-style text editor.

        Blyant (Danish for pencil) is version 1.4.3. Inside Blyant, ? shows the keys.

        ## Pages

        - [Blyant](https://blyant-editor.com/): The editor.
        - [Download](https://blyant-editor.com/download/): Download Blyant v1.4.3 for Windows, Linux
          and macOS, with nothing else to install.
        - [Guide](docs/guide.md)

        ## Optional

        - [Changelog](https://blyant-editor.com/changelog/)
        """;

    [Fact]
    public void LlmsTxt_InTheFormat_HasNoProblems()
    {
        Assert.Empty(Validate(Llms, "llms.txt"));
    }

    [Theory]
    [InlineData("Intro first\n\n# Name", "llms.missing-h1")]
    [InlineData("## Pages\n\n- [a](https://a.org/)", "llms.missing-h1")]
    [InlineData("# Name\n\n# Other", "llms.multiple-h1")]
    [InlineData("# Name\n\n## Pages\n\n### Sub", "llms.heading-level")]
    [InlineData("# Name\n\n## Pages\n\nSome text in a section.", "llms.section-content")]
    [InlineData("# Name\n\n## Pages\n\n- Just text", "llms.list-item")]
    [InlineData("# Name\n\n## Pages\n\n- [a](my page.html)", "llms.bad-url")]
    [InlineData("# Name\n\n## Pages\n\n- [a]()", "llms.bad-url")]
    public void LlmsTxt_OutOfFormat_IsAnError(string text, string code)
    {
        Assert.Contains(Validate(text, "llms.txt"), d => d.Code == code && d.Severity == Severity.Error);
    }

    [Fact]
    public void LlmsTxt_DuplicateUrl_IsAWarning()
    {
        var d = Assert.Single(Validate("# N\n\n## A\n\n- [a](https://a.org/)\n- [b](https://a.org/)", "llms.txt"));
        Assert.Equal(("llms.duplicate-url", Severity.Warning), (d.Code, d.Severity));
    }

    [Fact]
    public void LlmsFullTxt_IsCheckedAsMarkdown_WithSeveralTitles()
    {
        Assert.Empty(Validate("# Page one\n\nText.\n\n# Page two\n\nSome text in no list.", "llms-full.txt"));
    }

    [Theory]
    [InlineData("llms.txt", DocumentKind.Markdown)]
    [InlineData("LLMS-FULL.TXT", DocumentKind.Markdown)]
    [InlineData("notes.txt", DocumentKind.Unknown)]
    [InlineData("guide.md", DocumentKind.Markdown)]
    [InlineData("a.markdown", DocumentKind.Markdown)]
    public void FileKinds_AreDetected(string name, DocumentKind kind)
    {
        Assert.Equal(kind, SourceDocument.DetectKind(name));
    }
}
