using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>A removed, renamed or moved configuration file must stay visible in the disclosure even when no policy remains.</summary>
public sealed class ConfigurationRemovalTests
{
    private const string Policy = "{ \"version\": 1, \"rules\": { \"readability.parameter-count\": { \"limit\": 2 } } }\n";
    private const string Three = "export function h(a: number, b: number, c: number) { return a + b + c; }\n";

    private static (GitFixture Repo, string Baseline) Repo()
    {
        var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n",
            ["tsconfig.json"] = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\"},\"include\":[\"**/*.ts\"]}\n",
            ["api.ts"] = "export const revision = 1;\n",
            [".codeshield.json"] = Policy,
        });
        repo.Write("api.ts", Three);
        return (repo, baseline);
    }

    private static async Task<JsonElement> EvaluateAsync(GitFixture repo, string baseline)
    {
        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    private static List<(string Path, string Change)> Files(JsonElement response) =>
        [.. response.GetProperty("configuration").GetProperty("files").EnumerateArray()
            .Select(file => (file.GetProperty("path").GetString()!.Replace('\\', '/'), file.GetProperty("change").GetString()!))];

    [Fact]
    public async Task RenamingTheConfigAwayIsDisclosedAndNoLongerAppliesThePolicy()
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        repo.Git("mv", ".codeshield.json", "disabled-policy.json");

        var response = await EvaluateAsync(repo, baseline);

        Assert.Equal(100, response.GetProperty("score").GetInt32());
        Assert.True(response.GetProperty("configuration").GetProperty("changed").GetBoolean());
        Assert.Equal([(".codeshield.json", "deleted")], Files(response));
    }

    [Fact]
    public async Task DeletingTheConfigIsDisclosed()
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        repo.Delete(".codeshield.json");

        var response = await EvaluateAsync(repo, baseline);

        Assert.Equal(100, response.GetProperty("score").GetInt32());
        Assert.True(response.GetProperty("configuration").GetProperty("changed").GetBoolean());
        Assert.Equal([(".codeshield.json", "deleted")], Files(response));
    }

    [Fact]
    public async Task MovingTheConfigToAnotherFolderReportsBothPathsAndTheNewScope()
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        Directory.CreateDirectory(Path.Combine(repo.Root, "other"));
        repo.Git("mv", ".codeshield.json", "other/.codeshield.json");

        var response = await EvaluateAsync(repo, baseline);

        Assert.Equal(100, response.GetProperty("score").GetInt32());
        Assert.Equal([(".codeshield.json", "deleted"), ("other/.codeshield.json", "renamed")], Files(response));
        Assert.True(response.GetProperty("configuration").GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task RenamingTheConfigAwayWithNoOtherChangeStillReportsIt()
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        repo.Write("api.ts", "export const revision = 1;\n");
        repo.Git("mv", ".codeshield.json", "disabled-policy.json");

        var response = await EvaluateAsync(repo, baseline);

        Assert.Equal("no_changes", response.GetProperty("status").GetString());
        Assert.Equal([(".codeshield.json", "deleted")], Files(response));
    }
}
