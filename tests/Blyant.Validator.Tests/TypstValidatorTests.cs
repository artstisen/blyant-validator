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
    [InlineData("= Title \\\nText *a\nb*\n")]
    [InlineData("= Title *a\nb* and _c\nd_\n")]
    [InlineData("这是*粗体*。 これは_強調_です。 한국어*굵게*.\n")]
    [InlineData("#image(\"fig-\" + str(1) + \".png\") #image(\"a.png\", width: 50%)\n")]
    public void WhatTypstCompiles_HasNoErrors(string text)
    {
        Assert.Empty(Errors(text));
        // (with the line endings of Windows, and of old Macs)
        Assert.Empty(Errors(text.Replace("\n", "\r\n")));
        Assert.Empty(Errors(text.Replace("\n", "\r")));
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
        Assert.Contains(code, Errors(text.Replace("\n", "\r\n")));
        Assert.Contains(code, Errors(text.Replace("\n", "\r")));
    }

    [Fact]
    public void AMarkBetweenLettersOfWords_IsText_AlsoOutsideTheFirstPlane()
    {
        // (U+1D400, a letter of two chars, on both sides)
        Assert.Empty(Errors("\U0001D400*\U0001D400\n"));
        Assert.Contains("typst.unclosed-delimiter", Errors("这是*粗体\n"));
    }

    [Fact]
    public void LabelsInCodeThatPlacesThem_AreThere_ButNotCounted()
    {
        Assert.Empty(Check("#if true [= A <intro>] else [= B <intro>]\nSee @intro.\n"));
        Assert.Empty(Check("#let title = [= Intro <intro>]\n#title\nSee @intro.\n"));
        // (in a #for it is there as many times as the loop runs, which is not known without running it: when that is
        // more than once the compiler tells, and this check does not)
        Assert.Empty(Check("#for i in range(2) [= Part <p>]\nSee @p.\n"));
    }

    [Fact]
    public void AnEscapedCharacter_InAPath_IsDecoded()
    {
        var asked = new List<string>();
        Check("#image(\"\\u{61}.png\")\n", new TypstFiles(p => { asked.Add(p); return true; }));
        Assert.Equal(Path.GetFullPath("a.png"), Assert.Single(asked));
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
        Assert.Equal("typst.too-deep", Assert.Single(Errors(text)));
        Assert.Equal("typst.too-deep", Assert.Single(Errors(string.Concat(Enumerable.Repeat("#box[", 300)) + "x" + new string(']', 300))));
    }

    // A folder of files given as texts: the folders listed in the order given (not sorted), as a file system may
    private static TypstFiles FilesOf(Dictionary<string, string> texts) =>
        new(texts.ContainsKey, p => texts.GetValueOrDefault(p), f => [.. texts.Keys.Where(k => Path.GetDirectoryName(k) == f)]);

    [Fact]
    public void AnIncludeFromTheRoot_IsFromTheFolderOfTheMainFile()
    {
        var folder = Path.GetFullPath("rooted");
        var main = Path.Combine(folder, "main.typ");
        var one = Path.Combine(folder, "ch", "one.typ");
        var texts = new Dictionary<string, string>
        {
            [main] = "= Intro <intro>\n#include \"/ch/one.typ\"\n",
            [one] = "See @intro.\n",
        };

        Assert.Empty(TypstValidator.Validate(one, texts[one], FilesOf(texts)));
        Assert.Equal(main, TypstValidator.MainFile(one, FilesOf(texts)));
    }

    [Fact]
    public void ANulInAPath_IsNotAFile_AndThrowsNothing()
    {
        var text = "#image(\"a\u0000b.png\")\n#include \"c\u0000.typ\"\nSee @x.\n";
        var codes = Check(text, new TypstFiles(_ => true, _ => "", _ => [])).Select(d => d.Code).ToList();
        Assert.DoesNotContain("typst.missing-file", codes);
    }

    [Fact]
    public void TheMainFile_IsChosenInTheSameOrder_OnEverySystem()
    {
        // (two files include the chapter, listed as a file system may: not by name)
        var folder = Path.GetFullPath("two-mains");
        var print = Path.Combine(folder, "print.typ");
        var book = Path.Combine(folder, "book.typ");
        var one = Path.Combine(folder, "one.typ");
        var texts = new Dictionary<string, string>
        {
            [print] = "#include \"one.typ\"\n",
            [one] = "= One\n",
            [book] = "#include \"one.typ\"\n",
        };

        Assert.Equal(book, TypstValidator.MainFile(one, FilesOf(texts)));
    }

    [Fact]
    public void AnIncludeInOtherCase_IsTheFile_UnlessTheFolderHasBoth()
    {
        var folder = Path.GetFullPath("cased");
        var main = Path.Combine(folder, "main.typ");
        var one = Path.Combine(folder, "one.typ");
        var texts = new Dictionary<string, string>
        {
            [main] = "= Intro <intro>\n#include \"One.typ\"\n",
            [one] = "See @intro.\n",
        };
        Assert.Equal(main, TypstValidator.MainFile(one, FilesOf(texts)));

        // (a file system that tells case apart, with both files: One.typ is another file)
        texts[Path.Combine(folder, "One.typ")] = "= Other\n";
        Assert.Equal(one, TypstValidator.MainFile(one, FilesOf(texts)));
    }

    [Fact]
    public void AFileIncludedByCode_LeavesTheReferencesUnchecked()
    {
        var folder = Path.GetFullPath("loop");
        var main = Path.Combine(folder, "main.typ");
        var one = Path.Combine(folder, "ch", "one.typ");
        var texts = new Dictionary<string, string>
        {
            [main] = "= Intro <intro>\n#for c in (\"ch/one.typ\",) { include c }\nSee @one.\n",
            [one] = "= One <one>\nSee @intro.\n",
        };

        Assert.Empty(TypstValidator.Validate(one, texts[one], FilesOf(texts)));
        Assert.Empty(TypstValidator.Validate(main, texts[main], FilesOf(texts)));
    }

    [Fact]
    public void TheDocument_IsThatOfTheMainFile_TwoFoldersUp()
    {
        var folder = Path.GetFullPath("deep");
        var main = Path.Combine(folder, "main.typ");
        var part = Path.Combine(folder, "parts", "part.typ");
        var one = Path.Combine(folder, "parts", "p1", "one.typ");
        var texts = new Dictionary<string, string>
        {
            [main] = "= Intro <intro>\n#include \"parts/part.typ\"\n",
            [part] = "#include \"p1/one.typ\"\n",
            [one] = "See @intro and @gone.\n",
        };

        var d = Assert.Single(TypstValidator.Validate(one, texts[one], FilesOf(texts)));
        Assert.Contains("gone", d.Message, StringComparison.Ordinal);
        Assert.Equal(main, TypstValidator.MainFile(one, FilesOf(texts)));
    }

    [Fact]
    public void TwoMainFiles_AreTwoDocuments_AndImportedLabelsAreNotPlaced()
    {
        var folder = Path.GetFullPath("mains");
        var book = Path.Combine(folder, "book.typ");
        var handout = Path.Combine(folder, "handout.typ");
        var one = Path.Combine(folder, "one.typ");
        var template = Path.Combine(folder, "tpl.typ");
        var texts = new Dictionary<string, string>
        {
            [book] = "#import \"tpl.typ\": *\n= Title <t>\n#include \"one.typ\"\n",
            [handout] = "= Title <t>\n#include \"one.typ\"\n",
            [one] = "See @t and @demo.\n",
            [template] = "= Demo <t>\n#let x = 1\n= Shown <demo>\n",
        };

        Assert.Empty(TypstValidator.Validate(one, texts[one], FilesOf(texts)));
    }

    [Fact]
    public void TypstFiles_AreKnown()
    {
        Assert.Equal(DocumentKind.Typst, SourceDocument.DetectKind("Book/main.TYP"));
    }
}
