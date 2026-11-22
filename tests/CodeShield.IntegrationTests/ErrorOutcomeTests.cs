using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Error, incomplete and suppression outcomes through the real MCP server.</summary>
public sealed class ErrorOutcomeTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"},\"include\":[\"*.ts\"]}\n";

    private static void AssertNoScore(JsonElement response, string status, string code)
    {
        Assert.Equal(status, response.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("passed").ValueKind);
        Assert.Contains(code, response.GetProperty("reasons").EnumerateArray().Select(r => r.GetProperty("code").GetString()));
    }

    private static (GitFixture Repo, string Baseline) TypeScriptRepo()
    {
        var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package, ["tsconfig.json"] = Tsconfig, ["a.ts"] = "export const a = 1;\n",
        });
        repo.Write("a.ts", "export const a = 2;\n");
        return (repo, baseline);
    }

    [Fact]
    public async Task MissingWorkspaceIsAnInvalidArgument()
    {
        await using var session = await McpSession.StartAsync();

        var response = await session.EvaluateAsync(Path.Combine(Path.GetTempPath(), "cs-does-not-exist-" + Guid.NewGuid().ToString("N")), "HEAD");

        AssertNoScore(response, "error", "INVALID_ARGUMENT");
    }

    [Fact]
    public async Task UnknownRulesetIsAnError()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        await using var session = await McpSession.StartAsync();

        var response = await session.EvaluateRulesetAsync(repo.Root, baseline, "nope@9.9.9");

        AssertNoScore(response, "error", "UNKNOWN_RULESET");
    }

    [Fact]
    public async Task NonRepositoryWorkspaceIsAGitFailure()
    {
        var directory = Directory.CreateTempSubdirectory("cs-nogit-");
        try
        {
            await using var session = await McpSession.StartAsync();

            var response = await session.EvaluateAsync(directory.FullName, "HEAD");

            AssertNoScore(response, "error", "GIT_FAILURE");
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Fact]
    public async Task MissingNodeRuntimeMakesTypeScriptEvaluationIncompleteWithMissingAnalyzer()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        await using var session = await McpSession.StartAsync(new Dictionary<string, string> { ["CODESHIELD_NODE"] = Path.Combine(repo.Root, "no-node.exe") });

        var response = await session.EvaluateAsync(repo.Root, baseline);

        AssertNoScore(response, "incomplete", "MISSING_ANALYZER");
    }

    [Fact]
    public async Task CSharpFileWithoutAProjectIsAnUnresolvedOwnership()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["README.md"] = "x\n", ["Loose.cs"] = "class Loose { }\n" });
        repo.Write("Loose.cs", "class Loose { int v = 1; }\n");
        await using var session = await McpSession.StartAsync();

        var response = await session.EvaluateAsync(repo.Root, baseline);

        AssertNoScore(response, "incomplete", "UNRESOLVED_OWNERSHIP");
    }

    [Fact]
    public async Task SuppressingASelectedTypeScriptRuleMakesTheEvaluationIncomplete()
    {
        var (repo, baseline) = TypeScriptRepo();
        using var _ = repo;
        repo.Write("a.ts", "// eslint-disable-next-line @typescript-eslint/no-floating-promises\nexport const a = 2;\n");
        await using var session = await McpSession.StartAsync();

        var response = await session.EvaluateAsync(repo.Root, baseline);

        AssertNoScore(response, "incomplete", "SUPPRESSED_RULE");
    }

    [Fact]
    public async Task MalformedProjectFileIsAProjectLoadFailure()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["Lib.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n", ["A.cs"] = "class A { }\n" });
        repo.Write("Lib.csproj", "<Project><oops</Project>\n");
        repo.Write("A.cs", "class A { int v; }\n");
        await using var session = await McpSession.StartAsync();

        var response = await session.EvaluateAsync(repo.Root, baseline);

        Assert.Equal(JsonValueKind.Null, response.GetProperty("score").ValueKind);
        Assert.NotEqual("complete", response.GetProperty("status").GetString());
        Assert.Contains(response.GetProperty("coverage").EnumerateArray().SelectMany(entry => entry.GetProperty("reasons").EnumerateArray()),
            reason => reason.GetProperty("code").GetString() == "PROJECT_LOAD_FAILED");
    }
}
