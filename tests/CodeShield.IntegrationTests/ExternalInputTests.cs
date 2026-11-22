using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>An external input that becomes resolvable only after the first discovery pass must make the snapshot stale.</summary>
public sealed class ExternalInputTests : IDisposable
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";

    private readonly string external = Path.Combine(Path.GetTempPath(), "cs-external-" + Guid.NewGuid().ToString("N"));

    public ExternalInputTests() => Directory.CreateDirectory(external);

    public void Dispose() => Directory.Delete(external, recursive: true);

    private string ExternalFile => Path.Combine(external, "shared.ts");

    private string Config() =>
        "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"," +
        $"\"paths\":{{\"@shared\":[{JsonSerializer.Serialize(ExternalFile.Replace('\\', '/'))}]}}}},\"include\":[\"*.ts\"]}}\n";

    private (GitFixture Repo, string Baseline) Repo()
    {
        var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package, ["tsconfig.json"] = Config(), ["a.ts"] = "export const a = 1;\n",
        });
        repo.Write("a.ts", "import '@shared';\nexport function six(a: number, b: number, c: number, d: number, e: number, f: number): number { return a + b + c + d + e + f; }\n");
        return (repo, baseline);
    }

    private async Task<int> WorkerRunsPerEvaluationAsync()
    {
        var (repo, baseline) = Repo();
        using var _ = repo;
        using var node = new ConcurrentEditNode(Path.Combine(external, "unused.txt"), int.MaxValue);
        await using var session = await McpSession.StartAsync(node.Environment);
        await session.EvaluateAsync(repo.Root, baseline);
        return node.Runs;
    }

    [Fact]
    public async Task AnExternalFileThatBecomesResolvableAfterAnalysisMakesTheSnapshotStale()
    {
        var runs = await WorkerRunsPerEvaluationAsync();
        var (repo, baseline) = Repo();
        using var _ = repo;
        using var node = new ConcurrentEditNode(ExternalFile, runs / 2);
        await using var session = await McpSession.StartAsync(node.Environment);

        var response = await session.EvaluateAsync(repo.Root, baseline);

        Assert.Equal("incomplete", response.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("passed").ValueKind);
        Assert.Equal("STALE_SNAPSHOT", AcceptanceTests.ReasonCode(response));
        Assert.Contains(response.GetProperty("findings").EnumerateArray(), f => f.GetProperty("ruleId").GetString() == "readability.parameter-count");
        Assert.NotEmpty(response.GetProperty("coverage").EnumerateArray());
        Assert.NotEmpty(response.GetProperty("analyzerVersions").EnumerateObject());
    }

    [Fact]
    public async Task AStableExternalInputIsNotStale()
    {
        File.WriteAllText(ExternalFile, "export const shared = 1;\n");
        var (repo, baseline) = Repo();
        using var _ = repo;
        await using var session = await McpSession.StartAsync();

        var response = await session.EvaluateAsync(repo.Root, baseline);

        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.Equal(95, response.GetProperty("score").GetInt32());
    }
}
