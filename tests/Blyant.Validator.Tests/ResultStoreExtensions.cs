using Blyant.Validator.Cli.Tui;

namespace Blyant.Validator.Tests;

/// <summary>What the tests of the TUI ask of its results</summary>
internal static class ResultStoreExtensions
{
    /// <summary>The result of a file, which the test expects to be there (it fails when there is none)</summary>
    internal static FileResult Result(this ResultStore store, string path)
    {
        var result = store.Get(path);
        Assert.NotNull(result);
        return result;
    }
}
