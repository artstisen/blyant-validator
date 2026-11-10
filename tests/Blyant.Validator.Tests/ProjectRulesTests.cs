using System.Text.RegularExpressions;

namespace Blyant.Validator.Tests;

/// <summary>Checks the rules in AGENTS.md that can be checked by reading the files of the repository.</summary>
public partial class ProjectRulesTests
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlyantValidator.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static IEnumerable<string> RepositoryFiles(params string[] extensions)
    {
        string[] skipped = ["bin", "obj", ".git", ".vs", ".idea", "TestResults", "publish"];
        return Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var relative = Path.GetRelativePath(Root, f);
                var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !parts.Any(p => skipped.Contains(p)) &&
                       (extensions.Length == 0 || extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            });
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}")]
    private static partial Regex EmailAddress();

    [Fact]
    public void NoEmailAddresses_ExceptGitHubNoreply()
    {
        var found = new List<string>();
        foreach (var file in RepositoryFiles())
        {
            var text = File.ReadAllText(file);
            foreach (Match m in EmailAddress().Matches(text))
            {
                if (m.Value.EndsWith("@users.noreply.github.com", StringComparison.OrdinalIgnoreCase)) continue;
                found.Add($"{Path.GetRelativePath(Root, file)}: an email address at offset {m.Index}");
            }
        }
        Assert.Empty(found);
    }

    [GeneratedRegex(@"File\.WriteAll(Text|Bytes|Lines)|FileMode\.Create\b(?!New)|FileMode\.Truncate")]
    private static partial Regex UnsafeWrite();

    [Fact]
    public void FilesAreNotWrittenInWaysThatEmptyThemFirst()
    {
        var found = RepositoryFiles(".cs")
            .Where(f => f.Contains(Path.DirectorySeparatorChar + "src" + Path.DirectorySeparatorChar))
            .SelectMany(f => File.ReadAllLines(f)
                .Select((line, i) => (line, i))
                .Where(x => UnsafeWrite().IsMatch(x.line) && !x.line.TrimStart().StartsWith("//") && !x.line.TrimStart().StartsWith("///"))
                .Select(x => $"{Path.GetRelativePath(Root, f)}:{x.i + 1}"))
            .ToList();
        Assert.Empty(found);
    }

    [Fact]
    public void SourceFiles_AreUtf8WithBomAndCrlf()
    {
        var wrong = new List<string>();
        foreach (var file in RepositoryFiles(".cs", ".csproj", ".props", ".slnx"))
        {
            var bytes = File.ReadAllBytes(file);
            var relative = Path.GetRelativePath(Root, file);
            if (bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF) wrong.Add($"{relative}: no BOM");
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\n' && (i == 0 || bytes[i - 1] != (byte)'\r'))
                {
                    wrong.Add($"{relative}: LF line ending");
                    break;
                }
            }
        }
        Assert.Empty(wrong);
    }

    [Fact]
    public void MarkdownFiles_HaveLf()
    {
        var wrong = RepositoryFiles(".md")
            .Where(f => File.ReadAllText(f).Contains('\r'))
            .Select(f => Path.GetRelativePath(Root, f))
            .ToList();
        Assert.Empty(wrong);
    }
}
