using Blyant.Validator.Cli;

namespace Blyant.Validator.Tests;

public sealed class DocumentIoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "blyant-validator-tests-" + Guid.NewGuid().ToString("N"));

    public DocumentIoTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SafeWrite_KeepsTheBom(bool bom)
    {
        var path = Path.Combine(_dir, "a.css");
        byte[] preamble = bom ? [0xEF, 0xBB, 0xBF] : [];
        await File.WriteAllBytesAsync(path, [.. preamble, .. "a{}"u8.ToArray()]);

        var document = await DocumentIo.LoadAsync(path, default);
        Assert.Equal(bom, document.HasUtf8Bom);
        Assert.Equal("a{}", document.Content);

        DocumentIo.SafeWriteAllText(path, "a {}\n", document.HasUtf8Bom);

        var bytes = await File.ReadAllBytesAsync(path);
        Assert.Equal([.. preamble, .. "a {}\n"u8.ToArray()], bytes);
        Assert.Single(Directory.GetFiles(_dir));
    }
}
