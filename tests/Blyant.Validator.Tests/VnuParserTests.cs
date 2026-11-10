using Blyant.Validator.Core;
using Blyant.Validator.Vnu;

namespace Blyant.Validator.Tests;

public class VnuParserTests
{
    [Fact]
    public void Parse_MapsTypesAndPositions()
    {
        const string json = """
            {"messages":[
              {"type":"error","lastLine":5,"lastColumn":17,"firstColumn":1,"message":"An img element must have an alt attribute."},
              {"type":"info","subType":"warning","lastLine":2,"firstLine":2,"lastColumn":16,"firstColumn":1,"message":"Consider adding a lang attribute."},
              {"type":"info","message":"Just so you know."}
            ]}
            """;

        var result = VnuValidator.Parse("a.html", json);

        Assert.Equal(3, result.Count);
        Assert.Equal(Severity.Error, result[0].Severity);
        Assert.Equal(new SourcePosition(5, 1), result[0].Start);
        Assert.Equal(new SourcePosition(5, 17), result[0].End);
        Assert.Equal(Severity.Warning, result[1].Severity);
        Assert.Equal("vnu.info.warning", result[1].Code);
        Assert.Equal(Severity.Info, result[2].Severity);
        Assert.All(result, d => Assert.Equal("vnu", d.Engine));
    }
}
