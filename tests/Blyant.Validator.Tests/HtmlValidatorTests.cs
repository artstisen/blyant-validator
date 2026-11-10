using Blyant.Validator.Core;
using Blyant.Validator.Html;

namespace Blyant.Validator.Tests;

public class HtmlValidatorTests
{
    private static async Task<IReadOnlyList<Diagnostic>> Validate(string html) =>
        await new HtmlValidator().ValidateAsync(new SourceDocument("test.html", html, DocumentKind.Html));

    [Fact]
    public async Task ValidDocument_HasNoDiagnostics()
    {
        var result = await Validate("""
            <!DOCTYPE html>
            <html lang="en">
            <head><title>Test</title></head>
            <body><img src="a.png" alt=""></body>
            </html>
            """);

        Assert.Empty(result);
    }

    [Fact]
    public async Task MissingAlt_IsReportedWithPosition()
    {
        var result = await Validate("""
            <!DOCTYPE html>
            <html lang="en">
            <head><title>Test</title></head>
            <body>
            <img src="a.png">
            </body>
            </html>
            """);

        var d = Assert.Single(result);
        Assert.Equal("html.img-missing-alt", d.Code);
        Assert.Equal(5, d.Start.Line);
    }

    [Fact]
    public async Task DuplicateId_IsReported()
    {
        var result = await Validate("""
            <!DOCTYPE html><html lang="en"><head><title>T</title></head>
            <body><p id="x"></p><p id="x"></p></body></html>
            """);

        Assert.Contains(result, d => d.Code == "html.duplicate-id");
    }

    [Fact]
    public async Task MissingTitleAndLang_AreReported()
    {
        var result = await Validate("<!DOCTYPE html><html><head></head><body></body></html>");

        Assert.Contains(result, d => d.Code == "html.head-missing-title" && d.Severity == Severity.Error);
        Assert.Contains(result, d => d.Code == "html.missing-lang" && d.Severity == Severity.Warning);
    }

    [Fact]
    public async Task ParseErrors_AreReported()
    {
        var result = await Validate("<html lang=\"en\"><head><title>T</title></head><body></body></html>");

        Assert.Contains(result, d => d.Code.StartsWith("html.parse.", StringComparison.Ordinal));
    }
}
