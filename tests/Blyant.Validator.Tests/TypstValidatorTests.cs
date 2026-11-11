using Blyant.Validator.Core;
using Blyant.Validator.Typst;

namespace Blyant.Validator.Tests;

/// <summary>
/// The Typst checks. The cases were tried with the Typst compiler (0.15): what it refuses is an error here, what it
/// takes is not.
/// </summary>
public class TypstValidatorTests
{
    private static IReadOnlyList<Diagnostic> Check(string text, TypstFiles? files = null) =>
        TypstValidator.Validate(Path.GetFullPath("doc.typ"), text.ReplaceLineEndings("\n"), files);

    private static IEnumerable<string> Errors(string text, TypstFiles? files = null) =>
        Check(text, files).Where(d => d.Severity == Severity.Error).Select(d => d.Code);

    [Theory]
    // Strong and emphasis: closed in their paragraph, or between letters (5*3, snake_case)
    [InlineData("5*3 and x*y*z\n")]
    [InlineData("*bold\nstill bold*\n")]
    [InlineData("_hi_ and a_b and snake_case_name\n")]
    [InlineData("- item *a\n  more* text\n")]
    [InlineData("- item *a\nmore*\n")]
    [InlineData("\\*not bold and \\_not emphasis\n")]
    [InlineData("He said \"hi\" `code*` $x*y$ *ok*\n")]
    [InlineData("// comment *\ntext\n")]
    [InlineData("/* *bold */ text\n")]
    [InlineData("*a // c*\nb*\n")]
    // Brackets in the text, links, labels, math
    [InlineData("[a] b and Hello [world\n")]
    [InlineData("See https://a.com/x_(y)_z. And *b*\n")]
    [InlineData("a < b and c > d\n")]
    [InlineData("Costs \\$5 and \\$10, or $5 and $10\n")]
    [InlineData("$[a, b)$ and $ sum_(i=0)^n i $ and $#calc.max(1, 2)$\n")]
    [InlineData("$x \"a $ b\" y$\n")]
    // Comments and raw text
    [InlineData("/* a /* b */ c */ text\n")]
    [InlineData("Text /* never closed\n")]
    [InlineData("`a\nb`\n")]
    [InlineData("`` empty and ```\nblock ` inside\n``` *ok*\n")]
    // Code
    [InlineData("#let s = \"a\nb\"\n")]
    [InlineData("#let x = (\n  1,\n  2)\nText\n")]
    [InlineData("#{\n  \"string with ] and )\"\n}\n")]
    [InlineData("#let x = true\n#if x [a] else [b] *c*\n")]
    [InlineData("#if true [a]\nelse [b]\n")]
    [InlineData("#if true {\n  [a]\n} else {\n  [b]\n}\nText *x*\n")]
    [InlineData("#for i in range(3) [#i ] done\n")]
    [InlineData("(p. #context counter(page).get()) and *b*\n")]
    [InlineData("#context { let x = 1; [#x] }\n")]
    [InlineData("#show heading: it => [\n  *#it.body*\n]\n= Head\n")]
    [InlineData("#let f(x) = {\n  if x { [a] } else { [b] }\n}\n#f(true)\n")]
    [InlineData("#let f = x => [*a*]\n#emph[_a_] #strong[*b*]\n")]
    [InlineData("#\"a//b\" and #{ \"}\" }\n")]
    [InlineData("Text \\\n next\n")]
    public void WhatTypstCompiles_HasNoErrors(string text)
    {
        Assert.Empty(Errors(text));
    }

    [Theory]
    [InlineData("Five * three is fifteen.\n", "typst.unclosed-delimiter")]
    [InlineData("Some *strong text\n\nNext paragraph.\n", "typst.unclosed-delimiter")]
    [InlineData("= Head *bold\n", "typst.unclosed-delimiter")]
    [InlineData("#strong[*x]\n", "typst.unclosed-delimiter")]
    [InlineData("- item *one\n- two\n", "typst.unclosed-delimiter")]
    [InlineData("Para *a\n= Head\n", "typst.unclosed-delimiter")]
    [InlineData("/ Term: def *x\n", "typst.unclosed-delimiter")]
    [InlineData("a*\n", "typst.unclosed-delimiter")]
    [InlineData("Hello ] world\n", "typst.unexpected-close")]
    [InlineData("#box[a]]\n", "typst.unexpected-close")]
    [InlineData("#[ x ] ] y\n", "typst.unexpected-close")]
    [InlineData("Code `x = 1\n", "typst.unclosed-raw")]
    [InlineData("```py\nprint(1)\n", "typst.unclosed-raw")]
    [InlineData("Sum $a + b\n", "typst.unclosed-delimiter")]
    [InlineData("#let x = \"unclosed\n", "typst.unclosed-string")]
    [InlineData("#text(red)[Hello\n", "typst.unclosed-delimiter")]
    [InlineData("#{ let a = 1\n", "typst.unclosed-delimiter")]
    [InlineData("#let x = (1, 2))\n", "typst.unexpected-close")]
    [InlineData("C# language\n", "typst.expected-expression")]
    public void WhatTypstRefuses_IsAnError(string text, string code)
    {
        Assert.Contains(code, Errors(text));
    }

    [Fact]
    public void AnErrorIsAtTheMarkThatIsNotClosed()
    {
        var d = Assert.Single(Check("Line one.\nFive * three.\n"));
        Assert.Equal(new SourcePosition(2, 6), d.Start);
    }

    [Fact]
    public void AHeadingWithoutItsSpace_IsAWarning()
    {
        var d = Assert.Single(Check("=Heading\n\n== Real heading\n"));
        Assert.Equal(("typst.heading-space", Severity.Warning), (d.Code, d.Severity));
    }

    [Fact]
    public void References_AreCheckedAgainstTheLabels()
    {
        Assert.Empty(Check("= Intro <intro>\nSee @intro and @intro[Part].\n"));
        Assert.Contains("typst.unknown-reference", Errors("See @missing.\n"));
        Assert.Contains("typst.ambiguous-reference", Errors("= A <intro>\n= B <intro>\nSee @intro.\n"));
        var duplicate = Assert.Single(Check("= A <intro>\n= B <intro>\n"));
        Assert.Equal(("typst.duplicate-label", Severity.Warning), (duplicate.Code, duplicate.Severity));
    }

    [Fact]
    public void AnAtSignInAnEmailAddress_IsTold()
    {
        // (the address is made here: the project has no e-mail addresses in its files)
        const string at = "@";
        var d = Assert.Single(Check("Mail me" + at + "example.com now\n"));
        Assert.Equal("typst.unknown-reference", d.Code);
        Assert.Contains("\\@", d.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("#bibliography(\"refs.bib\")\nSee @knuth.\n")]
    [InlineData("#metadata(1) #label(\"x\")\nSee @x.\n")]
    [InlineData("#import \"@preview/template:1.0.0\": *\nSee @x.\n")]
    public void References_ThatCannotBeFollowed_AreNotChecked(string text)
    {
        Assert.DoesNotContain("typst.unknown-reference", Errors(text, new TypstFiles(_ => true, _ => null, _ => [])));
    }

    [Fact]
    public void LabelsOfIncludedAndIncludingFiles_AreInTheDocument()
    {
        var folder = Path.GetFullPath("book");
        var main = Path.Combine(folder, "main.typ");
        var one = Path.Combine(folder, "chapters", "one.typ");
        var two = Path.Combine(folder, "chapters", "two.typ");
        var texts = new Dictionary<string, string>
        {
            [main] = "#include \"chapters/one.typ\"\n#include \"chapters/two.typ\"\n",
            [one] = "= One <one>\nSee @two.\n",
            [two] = "= Two <two>\nSee @one and @three.\n",
        };
        var files = new TypstFiles(texts.ContainsKey, p => texts.GetValueOrDefault(p),
            f => [.. texts.Keys.Where(k => Path.GetDirectoryName(k) == f)]);

        Assert.Empty(TypstValidator.Validate(one, texts[one], files));
        var d = Assert.Single(TypstValidator.Validate(two, texts[two], files));
        Assert.Equal("typst.unknown-reference", d.Code);
        Assert.Contains("three", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LabelsOfFilesBeside_Count_WhenNoFileIncludesIt()
    {
        // (chapters whose main file is somewhere else: their document is put together there)
        var folder = Path.GetFullPath("chapters");
        var one = Path.Combine(folder, "one.typ");
        var two = Path.Combine(folder, "two.typ");
        var texts = new Dictionary<string, string>
        {
            [one] = "= One <one>\nSee @two and @three.\n",
            [two] = "= Two <two>\n",
        };
        var files = new TypstFiles(texts.ContainsKey, p => texts.GetValueOrDefault(p),
            f => [.. texts.Keys.Where(k => Path.GetDirectoryName(k) == f)]);

        var d = Assert.Single(TypstValidator.Validate(one, texts[one], files));
        Assert.Contains("three", d.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Files_AreCheckedThroughTheGivenFunction()
    {
        var asked = new List<string>();
        bool Exists(string path)
        {
            asked.Add(path);
            return path.EndsWith("cover.png", StringComparison.Ordinal);
        }

        var text = "#image(\"img/cover.png\") #image(\"img/gone.png\")\n#include \"ch\\\\one.typ\"\n#image(\"/root.png\") #image(variable)\n#bibliography(\"refs.bib\")\n";
        var errors = Check(text, new TypstFiles(Exists)).Where(d => d.Code == "typst.missing-file").ToList();
        Assert.Equal(3, errors.Count); // gone.png, one.typ and refs.bib
        Assert.Contains(Path.GetFullPath(Path.Combine("img", "cover.png")), asked);
        Assert.Contains(Path.GetFullPath(Path.Combine("ch", "one.typ")), asked);
        Assert.DoesNotContain(asked, p => p.EndsWith("root.png", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("#import \"@preview/cetz:0.3.4\": canvas\n", false)]
    [InlineData("#import \"@local/mine:1.0.0\"\n", false)]
    [InlineData("#import \"@preview/bad pkg:1.0.0\": *\n", true)]
    [InlineData("#import \"@preview/cetz:0.3\": canvas\n", true)]
    public void Packages_AreCheckedForTheirForm_AndNeverFetched(string text, bool error)
    {
        Assert.Equal(error, Errors(text, new TypstFiles(_ => false)).Contains("typst.bad-package"));
    }

    [Fact]
    public void DeepNesting_IsReported_NotOverflowed()
    {
        var text = string.Concat(Enumerable.Repeat("#box[", 5000));
        Assert.Contains("typst.too-deep", Errors(text));
    }

    [Fact]
    public void TypstFiles_AreKnown()
    {
        Assert.Equal(DocumentKind.Typst, SourceDocument.DetectKind("Book/main.TYP"));
    }
}
