using CodeShield.Abstractions;
using CodeShield.Core.Duplication;

namespace CodeShield.Core.Tests;

public sealed class DuplicateFinderTests
{
    private static TokenFile File(string path, int tokens, int lines, int seed = 0)
    {
        var interned = new Dictionary<string, int>();
        int Intern(Token token) => interned.TryGetValue(token.Text, out var id) ? id : interned[token.Text] = interned.Count;
        var list = Enumerable.Range(0, tokens)
            .Select(i => new Token("id", $"t{(i * 7 + seed) % 23}", 1 + i * lines / tokens, i, 1 + i * lines / tokens, i + 1, i))
            .ToList();
        return new TokenFile(path, list, Intern);
    }

    private static IReadOnlyList<TokenMatch> Find(params TokenFile[] files) => new DuplicateFinder(files, (_, _) => true).Find();

    [Theory]
    [InlineData(49, 12, 0)]
    [InlineData(50, 9, 0)]
    [InlineData(50, 10, 1)]
    [InlineData(80, 12, 1)]
    public void MatchRequiresFiftyTokensAndTenCodeLines(int tokens, int lines, int expected)
    {
        Assert.Equal(expected, Find(File("a", tokens, lines), File("b", tokens, lines)).Count);
    }

    [Fact]
    public void ManyIdenticalCopiesAreAllMatchedWithoutBucketCap()
    {
        var files = Enumerable.Range(0, 70).Select(i => File($"f{i:00}", 60, 12)).ToArray();
        var matches = Find(files);
        Assert.Contains(matches, match => match.FileA == 0 && match.FileB == 69);
        Assert.Equal(70 * 69 / 2, matches.Count(match => match.Length == 60));
    }

    [Fact]
    public void UnchangedOnlyDuplicatesAreIgnored()
    {
        var files = new[] { File("a", 60, 12), File("b", 60, 12) };
        Assert.Empty(new DuplicateFinder(files, (_, _) => false).Find());
    }
}
