using System.Text.Json;

namespace CodeShield.IntegrationTests;

public sealed class ScopeTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\"},\"include\":[\"*.ts\"]}\n";
    private const string SixParameters = "export function f(a: number, b: number, c: number, d: number, e: number, g: number) { return a + b + c + d + e + g; }\n";

    private static (GitFixture Repo, string Baseline) TypeScriptRepo()
    {
        var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package,
            ["tsconfig.json"] = Tsconfig,
            ["api.ts"] = "export const revision = 1;\n",
            ["README.md"] = "# fixture\n",
        });
        return (repo, baseline);
    }

    [Fact]
    public async Task DocumentationOnlyChangeReportsNoChanges()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write("README.md", "# changed\n");
        await using var session = await McpSession.StartAsync();
        var response = await session.EvaluateAsync(repo.Root, baseline);
        Assert.Equal("no_changes", response.GetProperty("status").GetString());
        Assert.Equal("NO_SUPPORTED_CHANGES", AcceptanceTests.ReasonCode(response));
        Assert.Equal(JsonValueKind.Null, response.GetProperty("score").ValueKind);
    }

    [Fact]
    public async Task UntrackedFileIsInScope()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write("extra.ts", SixParameters);
        await using var session = await McpSession.StartAsync();
        var response = await session.EvaluateAsync(repo.Root, baseline);
        Assert.Equal(95, response.GetProperty("score").GetInt32());
        Assert.Contains(response.GetProperty("scope").EnumerateArray(), e => e.GetProperty("path").GetString() == "extra.ts");
    }

    [Fact]
    public async Task RenamedAndEditedFileIsScoredAtItsNewPath()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write("api.ts", SixParameters);
        var legacy = repo.Commit(new Dictionary<string, string>());
        repo.Git("mv", "api.ts", "renamed.ts");
        repo.Write("renamed.ts", SixParameters.Replace("a + b", "a * b", StringComparison.Ordinal));
        await using var session = await McpSession.StartAsync();
        var response = await session.EvaluateAsync(repo.Root, legacy);
        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.Equal("renamed.ts", response.GetProperty("findings")[0].GetProperty("locations")[0].GetProperty("file").GetString());
        Assert.NotEqual(baseline, legacy);
    }

    [Fact]
    public async Task DeletedSourceIsNotScored()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Delete("api.ts");
        await using var session = await McpSession.StartAsync();
        var response = await session.EvaluateAsync(repo.Root, baseline);
        Assert.Equal(100, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task InputChangedDuringEvaluationIsStaleButKeepsEvidence()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write("extra.ts", SixParameters);
        using var node = new ConcurrentEditNode(Path.Combine(repo.Root, "extra.ts"));
        await using var session = await McpSession.StartAsync(node.Environment);
        var response = await session.EvaluateAsync(repo.Root, baseline);
        Assert.Equal("incomplete", response.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("passed").ValueKind);
        Assert.Equal("STALE_SNAPSHOT", AcceptanceTests.ReasonCode(response));
        Assert.NotEmpty(response.GetProperty("findings").EnumerateArray());
    }

    [Fact]
    public async Task InvalidBaselineAndContractVersionAreErrors()
    {
        var (repo, _) = TypeScriptRepo();
        using var _ = repo;
        await using var session = await McpSession.StartAsync();
        var badBaseline = await session.EvaluateAsync(repo.Root, "no-such-revision");
        Assert.Equal("error", badBaseline.GetProperty("status").GetString());
        Assert.Equal("INVALID_BASELINE", AcceptanceTests.ReasonCode(badBaseline));
        var badVersion = await session.EvaluateAsync(repo.Root, "HEAD", contractVersion: "2");
        Assert.Equal("UNSUPPORTED_CONTRACT_VERSION", AcceptanceTests.ReasonCode(badVersion));
    }
}
