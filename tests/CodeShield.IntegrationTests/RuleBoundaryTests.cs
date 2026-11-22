using System.Text;
using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Trigger/non-trigger boundaries of the structural rules, asserted through the real MCP server on TypeScript fixtures.</summary>
public sealed class RuleBoundaryTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"},\"include\":[\"*.ts\"]}\n";

    private static (GitFixture Repo, string Baseline) Repo(IReadOnlyDictionary<string, string>? extra = null)
    {
        var repo = new GitFixture();
        var files = new Dictionary<string, string> { ["package.json"] = Package, ["tsconfig.json"] = Tsconfig, ["README.md"] = "# f\n" };
        foreach (var (path, text) in extra ?? new Dictionary<string, string>())
        {
            files[path] = text;
        }

        return (repo, repo.Commit(files));
    }

    private static async Task<JsonElement> EvaluateAsync(GitFixture repo, string baseline)
    {
        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    private static List<JsonElement> Findings(JsonElement response, string ruleId) =>
        [.. response.GetProperty("findings").EnumerateArray().Where(f => f.GetProperty("ruleId").GetString() == ruleId)];

    private static string Branches(int count)
    {
        var body = new StringBuilder("export function f(x: number): number {\n  let r = 0;\n");
        for (var i = 0; i < count; i++)
        {
            body.Append(System.Globalization.CultureInfo.InvariantCulture, $"  if (x === {i}) {{ r += {i}; }}\n");
        }

        return body.Append("  return r;\n}\n").ToString();
    }

    private static string Nested(int depth)
    {
        var body = new StringBuilder("export function f(x: number): number {\n  let r = 0;\n");
        for (var i = 0; i < depth; i++)
        {
            body.Append(' ', 2 + i * 2).Append(System.Globalization.CultureInfo.InvariantCulture, $"if (x > {i}) {{\n");
        }

        body.Append(' ', 4 + depth * 2).Append("r += 1;\n");
        for (var i = depth - 1; i >= 0; i--)
        {
            body.Append(' ', 2 + i * 2).Append("}\n");
        }

        return body.Append("  return r;\n}\n").ToString();
    }

    private static string Lines(int bodyLines)
    {
        var body = new StringBuilder("export function f(): number {\n");
        for (var i = 0; i < bodyLines; i++)
        {
            body.Append(System.Globalization.CultureInfo.InvariantCulture, $"  const v{i} = {i};\n");
        }

        return body.Append("  return 0;\n}\n").ToString();
    }

    private static string Block(string suffix, int statements) =>
        $"export function shared{suffix}(a: number, b: number): number {{\n"
        + string.Concat(Enumerable.Range(0, statements).Select(i => $"  const value{i} = a * {i + 2} + b - {i};\n"))
        + "  return a + b;\n}\n";

    public static TheoryData<string, string, bool> Cases => new()
    {
        { "complexity.cyclomatic", Branches(5), false },
        { "complexity.cyclomatic", Branches(6), true },
        { "complexity.nesting", Nested(2), false },
        { "complexity.nesting", Nested(3), true },
        { "complexity.method-length", Lines(37), false },
        { "complexity.method-length", Lines(38), true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ThresholdTriggersExactlyAboveTheLimit(string ruleId, string source, bool triggers)
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        repo.Write("unit.ts", source);
        var response = await EvaluateAsync(repo, baseline);
        Assert.Equal(triggers, Findings(response, ruleId).Count == 1);
        Assert.Equal(triggers ? 88 + (ruleId == "complexity.cyclomatic" ? 0 : 1) : 100, response.GetProperty("score").GetInt32());
    }

    [Theory]
    [InlineData(400, false)]
    [InlineData(401, true)]
    public async Task NewFileOverFourHundredCodeLinesIsFlagged(int constants, bool flagged)
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        repo.Write("big.ts", string.Concat(Enumerable.Range(0, constants).Select(i => $"export const c{i} = {i};\n")));
        var response = await EvaluateAsync(repo, baseline);
        Assert.Equal(flagged, Findings(response, "structure.file-length").Count == 1);
    }

    [Fact]
    public async Task DuplicateBlocksAreOneChargedGroupWithAllOccurrences()
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        repo.Write("one.ts", Block("", 12));
        repo.Write("two.ts", Block("", 12));
        var response = await EvaluateAsync(repo, baseline);
        var finding = Assert.Single(Findings(response, "structure.duplication"));
        Assert.Equal(2, finding.GetProperty("locations").GetArrayLength());
        Assert.Equal(88, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task BlocksBelowTheMinimumsAreNotDuplicates()
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        repo.Write("one.ts", Block("", 3));
        repo.Write("two.ts", Block("", 3));
        var response = await EvaluateAsync(repo, baseline);
        Assert.Empty(Findings(response, "structure.duplication"));
        Assert.Equal(100, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task SeventyCopiesFormOneGroupWithoutAnyBucketCap()
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        foreach (var index in Enumerable.Range(0, 70))
        {
            repo.Write($"copy{index:00}.ts", Block("", 12));
        }

        var response = await EvaluateAsync(repo, baseline);
        var finding = Assert.Single(Findings(response, "structure.duplication"));
        Assert.Equal(70, finding.GetProperty("locations").GetArrayLength());
    }

    [Fact]
    public async Task NewImportCycleIsChargedAndTypeOnlyImportsAreNot()
    {
        var (repo, baseline) = Repo(new Dictionary<string, string> { ["a.ts"] = "export const a = 1;\n", ["b.ts"] = "export const b = 2;\n" });
        using var _ = repo;
        repo.Write("a.ts", "import { b } from './b';\nexport const a = b + 1;\n");
        repo.Write("b.ts", "import type { a } from './a';\nexport const b: typeof a = 2 as never;\n");
        var typeOnly = await EvaluateAsync(repo, baseline);
        Assert.Empty(Findings(typeOnly, "coupling.typescript-file-cycle"));

        repo.Write("b.ts", "import { a } from './a';\nexport const b = a + 1;\n");
        var cyclic = await EvaluateAsync(repo, baseline);
        Assert.Single(Findings(cyclic, "coupling.typescript-file-cycle"));
        Assert.Equal(85, cyclic.GetProperty("score").GetInt32());
    }
}
