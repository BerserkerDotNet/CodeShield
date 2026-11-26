using System.Globalization;
using System.Text;

namespace CodeShield.IntegrationTests;

/// <summary>Trigger/non-trigger boundaries of the structural rules on Kotlin sources, through the real MCP server and Kotlin worker.</summary>
public sealed class KotlinRuleBoundaryTests
{
    private const string Unit = "src/main/kotlin/app/Unit.kt";

    private static string Branches(int count)
    {
        var body = new StringBuilder("package app\n\nfun f(x: Int): Int {\n    var r = 0\n");
        for (var i = 0; i < count; i++)
        {
            body.Append(CultureInfo.InvariantCulture, $"    if (x == {i}) {{ r += {i} }}\n");
        }

        return body.Append("    return r\n}\n").ToString();
    }

    private static string Nested(int depth)
    {
        var body = new StringBuilder("package app\n\nfun f(x: Int): Int {\n    var r = 0\n");
        for (var i = 0; i < depth; i++)
        {
            body.Append(' ', 4 + i * 4).Append(CultureInfo.InvariantCulture, $"if (x > {i}) {{\n");
        }

        body.Append(' ', 4 + depth * 4).Append("r += 1\n");
        for (var i = depth - 1; i >= 0; i--)
        {
            body.Append(' ', 4 + i * 4).Append("}\n");
        }

        return body.Append("    return r\n}\n").ToString();
    }

    private static string Lines(int bodyLines)
    {
        var body = new StringBuilder("package app\n\nfun f(): Int {\n");
        for (var i = 0; i < bodyLines; i++)
        {
            body.Append(CultureInfo.InvariantCulture, $"    val v{i} = {i}\n");
        }

        return body.Append("    return 0\n}\n").ToString();
    }

    private static string Parameters(int count)
    {
        var names = Enumerable.Range(0, count).Select(i => $"p{i}").ToList();
        return $"package app\n\nfun f({string.Join(", ", names.Select(name => name + ": Int"))}): Int = {string.Join(" + ", names)}\n";
    }

    private static string Block(string package, int statements) =>
        $"package {package}\n\nfun shared(a: Int, b: Int): Int {{\n"
        + string.Concat(Enumerable.Range(0, statements).Select(i => $"    val value{i} = a * {i + 2} + b - {i}\n"))
        + "    return a + b\n}\n";

    public static TheoryData<string, string, bool, int> Cases => new()
    {
        { "complexity.cyclomatic", Branches(5), false, 100 },
        { "complexity.cyclomatic", Branches(6), true, 88 },
        { "complexity.nesting", Nested(2), false, 100 },
        { "complexity.nesting", Nested(3), true, 89 },
        { "complexity.method-length", Lines(37), false, 100 },
        { "complexity.method-length", Lines(38), true, 89 },
        { "readability.parameter-count", Parameters(5), false, 100 },
        { "readability.parameter-count", Parameters(6), true, 95 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ThresholdTriggersExactlyAboveTheLimit(string ruleId, string source, bool triggers, int score)
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        using var _ = repo;
        repo.Write(Unit, source);

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        var findings = KotlinFixture.Findings(response, ruleId);
        Assert.Equal(triggers ? 1 : 0, findings.Count);
        Assert.All(findings, finding => Assert.Equal(Unit, KotlinFixture.FileOf(finding)));
        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.Equal(score, response.GetProperty("score").GetInt32());
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, true)]
    public async Task NewFileOverFourHundredCodeLinesIsFlagged(int constants, bool flagged)
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        using var _ = repo;
        repo.Write("src/main/kotlin/Big.kt", string.Concat(Enumerable.Range(0, constants).Select(i => $"val c{i} = {i}\n")));

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        Assert.Equal(flagged, KotlinFixture.Findings(response, "structure.file-length").Count == 1);
    }

    [Fact]
    public async Task DuplicateBlocksAcrossFilesAreOneChargedGroup()
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        using var _ = repo;
        repo.Write("src/main/kotlin/one/One.kt", Block("one", 12));
        repo.Write("src/main/kotlin/two/Two.kt", Block("two", 12));

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        var finding = Assert.Single(KotlinFixture.Findings(response, "structure.duplication"));
        Assert.Equal(2, finding.GetProperty("locations").GetArrayLength());
        Assert.Equal(88, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task BlocksBelowTheMinimumsAreNotDuplicates()
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        using var _ = repo;
        repo.Write("src/main/kotlin/one/One.kt", Block("one", 3));
        repo.Write("src/main/kotlin/two/Two.kt", Block("two", 3));

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        Assert.Empty(KotlinFixture.Findings(response, "structure.duplication"));
        Assert.Equal(100, response.GetProperty("score").GetInt32());
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public async Task ClassWithDisjointStateGroupsHasLowCohesion(bool connected, int components)
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        using var _ = repo;
        repo.Write("src/main/kotlin/app/Counters.kt",
            "package app\n\nclass Counters(private var left: Int) {\n    private var right = 0\n\n"
            + "    fun nextLeft(): Int {\n        left += 1\n        return left\n    }\n\n"
            + "    fun nextRight(): Int {\n        right += 1\n        return right\n    }\n"
            + (connected ? "\n    fun total(): Int = left + right\n" : string.Empty)
            + "}\n");

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        var findings = KotlinFixture.Findings(response, "cohesion.lcom4");
        if (components == 1)
        {
            Assert.Empty(findings);
            Assert.Equal(100, response.GetProperty("score").GetInt32());
        }
        else
        {
            var finding = Assert.Single(findings);
            Assert.Equal(components, finding.GetProperty("actual").GetInt32());
            Assert.Equal(12, finding.GetProperty("deduction").GetInt32());
            Assert.Equal(88, response.GetProperty("score").GetInt32());
        }
    }
}
