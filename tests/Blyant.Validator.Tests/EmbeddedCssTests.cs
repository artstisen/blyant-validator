using Blyant.Validator.Core;
using Blyant.Validator.Html;

namespace Blyant.Validator.Tests;

public class EmbeddedCssTests
{
    private static async Task<IReadOnlyList<Diagnostic>> Validate(string html) =>
        await new HtmlValidator().ValidateAsync(new SourceDocument("t.html", html.ReplaceLineEndings("\n"), DocumentKind.Html));

    private const string Top = "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<title>T</title>\n";

    [Fact]
    public async Task StyleElement_IsValidated_AtItsPlaceInTheFile()
    {
        var result = await Validate(Top + "<style>\nbody {\n  colr: red;\n}\n</style>\n</head><body></body></html>");

        var d = Assert.Single(result);
        Assert.Equal("css.unknown-property", d.Code);
        Assert.Equal(new SourcePosition(7, 3), d.Start);
    }

    [Fact]
    public async Task StyleElement_OnTheSameLine_KeepsTheColumn()
    {
        var result = await Validate(Top + "  <style>p { margin: 10 px }</style>\n</head><body></body></html>");

        var d = Assert.Single(result);
        Assert.Equal("css.invalid-value", d.Code);
        Assert.Equal(new SourcePosition(5, 22), d.Start);
    }

    [Fact]
    public async Task StyleAttribute_IsValidatedAsDeclarations()
    {
        var result = await Validate(Top + "</head><body>\n<p style=\"color: red; widht: 10px\">x</p>\n</body></html>");

        var d = Assert.Single(result);
        Assert.Equal("css.unknown-property", d.Code);
        Assert.Contains("width", d.Message);
        // <p style=" is 10 characters: the value starts at column 11, and "widht" is 12 characters into it
        Assert.Equal(new SourcePosition(6, 23), d.Start);
    }

    [Theory]
    [InlineData("<style>a { color: var(--x) } .b { display: grid }</style>")]
    [InlineData("<style type=\"text/x-template\">not css at all {{</style>")]
    [InlineData("<style></style>")]
    public async Task ValidOrNotCss_HasNoProblems(string style)
    {
        Assert.Empty(await Validate(Top + style + "</head><body><p style=\"\">x</p><p style=\"margin:0 auto\">y</p></body></html>"));
    }

    [Fact]
    public async Task RuleInStyleAttribute_IsAnError()
    {
        var result = await Validate(Top + "</head><body><p style=\"a { color: red }\">x</p></body></html>");
        Assert.Contains(result, d => d.Code == "css.unexpected-rule");
    }
}
