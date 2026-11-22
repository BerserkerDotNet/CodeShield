using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Spec 3.1: a tracked path removed from the index but still present on disk is judged by its working bytes against the baseline blob.</summary>
public sealed class IndexOnlyRemovalTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"},\"include\":[\"*.ts\"]}\n";
    private const string Clean = "export function api(a: number): number {\n  return a;\n}\n";
    private const string SixParameters = "export function api(a: number, b: number, c: number, d: number, e: number, f: number): number {\n  return a + b + c + d + e + f;\n}\n";

    private static (GitFixture Repo, string Baseline) Setup()
    {
        var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["package.json"] = Package, ["tsconfig.json"] = Tsconfig, ["api.ts"] = Clean });
        repo.Git("rm", "--cached", "-q", "--", "api.ts");
        return (repo, baseline);
    }

    private static int ParameterFindings(JsonElement response) =>
        response.GetProperty("findings").EnumerateArray().Count(finding => finding.GetProperty("ruleId").GetString() == "readability.parameter-count");

    [Fact]
    public async Task UnchangedWorkingBytesAreNoChangesBeforeAndAfterRestaging()
    {
        var (repo, baseline) = Setup();
        using var _ = repo;
        await using var session = await McpSession.StartAsync();

        var before = await session.EvaluateAsync(repo.Root, baseline);
        repo.Stage("api.ts");
        var after = await session.EvaluateAsync(repo.Root, baseline);

        Assert.Equal("no_changes", before.GetProperty("status").GetString());
        Assert.Equal("no_changes", after.GetProperty("status").GetString());
    }

    [Fact]
    public async Task EditedWorkingBytesScoreTheSameBeforeAndAfterRestaging()
    {
        var (repo, baseline) = Setup();
        using var _ = repo;
        repo.Write("api.ts", SixParameters);
        var indexBefore = repo.Git("ls-files", "-s");
        await using var session = await McpSession.StartAsync();

        var before = await session.EvaluateAsync(repo.Root, baseline);

        Assert.Equal(indexBefore, repo.Git("ls-files", "-s"));
        Assert.Equal(SixParameters, File.ReadAllText(Path.Combine(repo.Root, "api.ts")));
        repo.Stage("api.ts");
        var after = await session.EvaluateAsync(repo.Root, baseline);

        foreach (var response in new[] { before, after })
        {
            Assert.Equal("complete", response.GetProperty("status").GetString());
            Assert.Equal(95, response.GetProperty("score").GetInt32());
            Assert.Equal(1, ParameterFindings(response));
        }
    }

    [Fact]
    public async Task UntrackedPathThatIsAlsoARenameSourceIsIncompleteNotAGitFailure()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["package.json"] = Package, ["tsconfig.json"] = Tsconfig, ["api.ts"] = Clean });
        repo.Git("mv", "api.ts", "moved.ts");
        repo.Write("api.ts", "export const other = 1;\n");
        await using var session = await McpSession.StartAsync();

        var response = await session.EvaluateAsync(repo.Root, baseline);

        Assert.NotEqual("error", response.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, response.GetProperty("reasons").ValueKind);
        Assert.DoesNotContain(response.GetProperty("reasons").EnumerateArray(), reason => reason.GetProperty("code").GetString() == "GIT_FAILURE");
    }
}
