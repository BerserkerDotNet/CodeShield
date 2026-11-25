using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Cascading .codeshield.json policy through the real MCP server against temporary Git repositories.</summary>
public sealed class ConfigurationTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\"},\"include\":[\"**/*.ts\"]}\n";
    private const string Six = "export function f(a: number, b: number, c: number, d: number, e: number, g: number) { return a + b + c + d + e + g; }\n";
    private const string Three = "export function h(a: number, b: number, c: number) { return a + b + c; }\n";
    private const string Tfm = "net10.0";

    private static (GitFixture Repo, string Baseline) TypeScriptRepo()
    {
        var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package,
            ["tsconfig.json"] = Tsconfig,
            ["api.ts"] = "export const revision = 1;\n",
        });
        return (repo, baseline);
    }

    private static string Limit(string ruleId, int limit, string? reason = null) =>
        $"{{ \"version\": 1, \"rules\": {{ \"{ruleId}\": {{ \"limit\": {limit}{(reason is null ? "" : $", \"reason\": \"{reason}\"")} }} }} }}\n";

    private static async Task<JsonElement> EvaluateAsync(GitFixture repo, string baseline)
    {
        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    [Fact]
    public async Task RootLimitRelaxesAndNestedStricterOverrideFailsOnlyItsFolder()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write(".codeshield.json", Limit("readability.parameter-count", 6, "legacy signatures"));
        repo.Write("strict/.codeshield.json", Limit("readability.parameter-count", 2));
        repo.Write("legacy.ts", Six);
        repo.Write("strict/new.ts", Three);

        var response = await EvaluateAsync(repo, baseline);

        Assert.Equal("complete", response.GetProperty("status").GetString());
        var finding = Assert.Single(response.GetProperty("findings").EnumerateArray());
        Assert.Equal("strict/new.ts", finding.GetProperty("locations")[0].GetProperty("file").GetString()!.Replace('\\', '/'));
        Assert.Equal(2, finding.GetProperty("limit").GetInt32());
        Assert.Equal(95, response.GetProperty("score").GetInt32());
        Assert.Equal(2, response.GetProperty("configuration").GetProperty("overrides").GetArrayLength());
    }

    [Fact]
    public async Task TypeScriptFolderExclusionRemovesFindingsAndDisclosesCounts()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write("legacy/.codeshield.json", "{ \"version\": 1, \"exclude\": true, \"reason\": \"vendored code\" }\n");
        repo.Write("legacy/old.ts", Six);

        var response = await EvaluateAsync(repo, baseline);

        Assert.Equal(100, response.GetProperty("score").GetInt32());
        Assert.Empty(response.GetProperty("findings").EnumerateArray());
        var configuration = response.GetProperty("configuration");
        var exclusion = Assert.Single(configuration.GetProperty("exclusions").EnumerateArray());
        Assert.Equal("vendored code", exclusion.GetProperty("reason").GetString());
        var dropped = Assert.Single(configuration.GetProperty("excludedFindings").EnumerateArray());
        Assert.Equal("readability.parameter-count", dropped.GetProperty("ruleId").GetString());
        Assert.Equal(1, dropped.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task UncommittedConfigIsAppliedAndReportedAsAdded()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write(".codeshield.json", Limit("readability.parameter-count", 6, "legacy"));
        repo.Write("legacy.ts", Six);

        var configuration = (await EvaluateAsync(repo, baseline)).GetProperty("configuration");

        var file = Assert.Single(configuration.GetProperty("files").EnumerateArray());
        Assert.Equal("added", file.GetProperty("change").GetString());
        Assert.True(configuration.GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task ConfigOnlyChangeReportsNoChangesWithDisclosure()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write(".codeshield.json", "{ \"version\": 1 }\n");

        var response = await EvaluateAsync(repo, baseline);

        Assert.Equal("no_changes", response.GetProperty("status").GetString());
        Assert.Contains("configuration", response.GetProperty("reasons")[0].GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(response.GetProperty("configuration").GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task EditingAConfigChangesTheSnapshotId()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write("legacy.ts", Six);
        repo.Write(".codeshield.json", Limit("readability.parameter-count", 6, "first"));
        var first = (await EvaluateAsync(repo, baseline)).GetProperty("snapshotId").GetString();
        repo.Write(".codeshield.json", Limit("readability.parameter-count", 6, "second"));
        var second = (await EvaluateAsync(repo, baseline)).GetProperty("snapshotId").GetString();
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData("{ \"version\": 1, \"exclude\": true }", "reason")]
    [InlineData("{ \"version\": 1, \"rules\": { \"nope.rule\": {} } }", "unknown rule")]
    [InlineData("{ \"version\": 1, \"rules\": { \"complexity.method-length\": { \"limit\": 2147483648, \"reason\": \"r\" } } }", "2147483647")]
    public async Task InvalidConfigurationReturnsAnErrorNamingTheFile(string json, string problem)
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write("pkg/.codeshield.json", json);
        repo.Write("legacy.ts", Six);

        var response = await EvaluateAsync(repo, baseline);

        Assert.Equal("error", response.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("score").ValueKind);
        var reason = response.GetProperty("reasons")[0];
        Assert.Equal("INVALID_CONFIGURATION", reason.GetProperty("code").GetString());
        Assert.Contains("pkg/.codeshield.json", reason.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains(problem, reason.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponsesWithoutConfigurationOmitTheBlock()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write("legacy.ts", Six);
        var response = await EvaluateAsync(repo, baseline);
        Assert.False(response.TryGetProperty("configuration", out var _));
        Assert.Equal(95, response.GetProperty("score").GetInt32());
    }

    private static string Project(string references = "") =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{Tfm}</TargetFramework></PropertyGroup>{references}</Project>\n";

    private static string Types(int interfaces) =>
        string.Concat(Enumerable.Range(0, 5).Select(i => i < interfaces ? $"public interface I{i} {{ }}\n" : $"public class C{i} {{ }}\n")) +
        string.Concat(Enumerable.Range(5, 5).Select(i => $"public class K{i} {{ }}\n"));

    private static (GitFixture Repo, string Baseline) KernelRepo()
    {
        var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["Subject/Subject.csproj"] = Project(),
            ["Subject/Types.cs"] = Types(5),
            ["Consumer/Consumer.csproj"] = Project("<ItemGroup><ProjectReference Include=\"..\\Subject\\Subject.csproj\" /></ItemGroup>"),
            ["Consumer/Use.cs"] = "public class Use { public int V() => 1; }\n",
        });
        repo.Write("Subject/Types.cs", Types(4));
        return (repo, baseline);
    }

    [Fact]
    public async Task KernelExemptionRemovesTheFindingMarksCoverageExcludedAndKeepsDependentEvidence()
    {
        var (repo, baseline) = KernelRepo();
        using var _ = repo;
        await using var session = await McpSession.StartAsync();
        var without = await session.EvaluateAsync(repo.Root, baseline);
        Assert.Equal(88, without.GetProperty("score").GetInt32());

        repo.Write("Subject/.codeshield.json",
            "{ \"version\": 1, \"rules\": { \"coupling.main-sequence-distance\": { \"enabled\": false, \"reason\": \"stable shared contract kernel\" } } }\n");
        var with = await session.EvaluateAsync(repo.Root, baseline);

        Assert.Equal("complete", with.GetProperty("status").GetString());
        Assert.Equal(100, with.GetProperty("score").GetInt32());
        Assert.Empty(with.GetProperty("findings").EnumerateArray());
        var excluded = with.GetProperty("coverage").EnumerateArray()
            .Where(entry => entry.GetProperty("status").GetString() == "excluded").ToList();
        Assert.Contains(excluded, entry => entry.GetProperty("ruleId").GetString() == "coupling.main-sequence-distance");
        Assert.DoesNotContain(excluded, entry => entry.GetProperty("ruleId").GetString() != "coupling.main-sequence-distance");
        var disclosure = with.GetProperty("configuration");
        Assert.Equal("coupling.main-sequence-distance", disclosure.GetProperty("excludedFindings")[0].GetProperty("ruleId").GetString());
        var details = await session.DetailsAsync(without.GetProperty("evaluationId").GetString()!, "coupling.main-sequence-distance");
        var evidence = details.GetProperty("findings")[0].GetProperty("evidence").EnumerateArray()
            .First(item => item.GetProperty("kind").GetString() == "mainSequence");
        Assert.Equal(1, evidence.GetProperty("afferent").GetInt32());
    }

    [Fact]
    public async Task ExcludedProjectThatFailsToLoadStillYieldsACompleteResult()
    {
        var (repo, baseline) = KernelRepo();
        using var _ = repo;
        repo.Write("Broken/Broken.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</Tar></PropertyGroup></Project>\n");
        repo.Write("Broken/Code.cs", "public class Code { }\n");
        repo.Write("Subject/.codeshield.json", "{ \"version\": 1, \"exclude\": true, \"reason\": \"kernel\" }\n");
        repo.Write("Broken/.codeshield.json", "{ \"version\": 1, \"exclude\": true, \"reason\": \"generated project\" }\n");

        var response = await EvaluateAsync(repo, baseline);

        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.Equal(100, response.GetProperty("score").GetInt32());
    }
}
