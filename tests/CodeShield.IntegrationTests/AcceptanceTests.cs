using System.Text.Json;

namespace CodeShield.IntegrationTests;

public sealed class AcceptanceTests
{
    private const string Tfm = "net10.0";

    [Fact]
    public async Task ServerExposesExactlyTheTwoContractTools()
    {
        await using var session = await McpSession.StartAsync();
        Assert.Equal(["evaluate_quality", "get_rule_details"], await session.ToolNamesAsync());
    }

    [Fact]
    public async Task TypeScript_StagedWorkingTreeThresholdAndDetailExpiry()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n",
            ["tsconfig.json"] = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\"},\"include\":[\"*.ts\"]}\n",
            ["api.ts"] = "export const revision = 1;\n",
        });
        repo.Write("api.ts", "export function first(a: number, b: number, c: number, d: number, e: number, f: number) { return a + b + c + d + e + f; }\nexport function last(a: string, b: string, c: string, d: string, e: string, f: string) { return [f, e, d, c, b, a].join(''); }\n");
        repo.Stage("api.ts");

        await using var session = await McpSession.StartAsync();
        var atThreshold = await session.EvaluateAsync(repo.Root, baseline);
        Assert.Equal("complete", atThreshold.GetProperty("status").GetString());
        Assert.Equal(90, atThreshold.GetProperty("score").GetInt32());
        Assert.True(atThreshold.GetProperty("passed").GetBoolean());
        var findings = Charged(atThreshold);
        Assert.Equal([1, 2], findings.Select(f => f.GetProperty("locations")[0].GetProperty("startLine").GetInt32()));
        Assert.All(findings, f =>
        {
            Assert.Equal("readability.parameter-count", f.GetProperty("ruleId").GetString());
            Assert.Equal(6, f.GetProperty("actual").GetInt32());
            Assert.Equal(5, f.GetProperty("limit").GetInt32());
            Assert.Equal(5, f.GetProperty("deduction").GetInt32());
        });

        repo.Write("api.ts", "export function total(values: readonly number[]) { return values.reduce((sum, value) => sum + value, 0); }\n");
        var workingTree = await session.EvaluateAsync(repo.Root, baseline);
        Assert.Equal(100, workingTree.GetProperty("score").GetInt32());
        Assert.Empty(Charged(workingTree));

        var details = await session.DetailsAsync(atThreshold.GetProperty("evaluationId").GetString()!, "readability.parameter-count");
        Assert.Equal("complete", details.GetProperty("status").GetString());
        Assert.Equal([1, 2], details.GetProperty("findings").EnumerateArray().Select(f => f.GetProperty("locations")[0].GetProperty("startLine").GetInt32()));

        var missingRule = await session.DetailsAsync(workingTree.GetProperty("evaluationId").GetString()!, "readability.parameter-count");
        Assert.Equal("RULE_NOT_FOUND", ReasonCode(missingRule));

        await session.DisposeAsync();
        await using var restarted = await McpSession.StartAsync();
        var expired = await restarted.DetailsAsync(atThreshold.GetProperty("evaluationId").GetString()!, "readability.parameter-count");
        Assert.Equal("EVALUATION_NOT_FOUND", ReasonCode(expired));
    }

    [Fact]
    public Task CSharp_PreExistingCohesionIsAttributedAndUntouchedLegacyIsNotScored() =>
        AssertPreExistingCohesionIsAttributedUntilConnected(
            new CohesionScenario(
                "Counters.cs",
                new Dictionary<string, string>
                {
                    ["Fixture.csproj"] = $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{Tfm}</TargetFramework></PropertyGroup></Project>\n",
                    ["Counters.cs"] = "public sealed class Counters\n{\n    private int left;\n    private int right;\n    public int NextLeft() => ++left;\n    public int NextRight() => ++right;\n}\n",
                    ["Other.cs"] = "public sealed class Other\n{\n    private int total;\n    private string text = \"\";\n    public void Reset() => total = 0;\n    public string Label() => text;\n}\n",
                },
                Edited: "public sealed class Counters\n{\n    private int left;\n    private int right;\n    public int NextLeft() => left += 2;\n    public int NextRight() => ++right;\n}\n",
                Connected: "public sealed class Counters\n{\n    private int left;\n    private int right;\n    public int NextLeft() => left += 2;\n    public int NextRight() => ++right;\n    public int Total() => left + right;\n}\n"),
            response => response);

    [Fact]
    public Task Kotlin_PreExistingCohesionIsAttributedAndUntouchedLegacyIsNotScored() =>
        AssertPreExistingCohesionIsAttributedUntilConnected(
            new CohesionScenario(
                "src/main/kotlin/app/Counters.kt",
                new Dictionary<string, string>
                {
                    ["build.gradle.kts"] = KotlinFixture.JvmBuild,
                    ["src/main/kotlin/app/Counters.kt"] = "package app\n\nclass Counters {\n    private var left = 0\n    private var right = 0\n    fun nextLeft() = ++left\n    fun nextRight() = ++right\n}\n",
                    ["src/main/kotlin/app/Other.kt"] = "package app\n\nclass Other {\n    private var total = 0\n    private var text = \"\"\n    fun reset() { total = 0 }\n    fun label() = text\n}\n",
                },
                Edited: "package app\n\nclass Counters {\n    private var left = 0\n    private var right = 0\n    fun nextLeft(): Int { left += 2; return left }\n    fun nextRight() = ++right\n}\n",
                Connected: "package app\n\nclass Counters {\n    private var left = 0\n    private var right = 0\n    fun nextLeft(): Int { left += 2; return left }\n    fun nextRight() = ++right\n    fun total() = left + right\n}\n"),
            KotlinFixture.RequireToolchain);

    /// <summary>
    /// Editing one member of a class that was already split (LCOM4 2) charges the pre-existing finding, while the equally split but
    /// untouched class is not scored; connecting the two halves clears it.
    /// </summary>
    private static async Task AssertPreExistingCohesionIsAttributedUntilConnected(CohesionScenario scenario, Func<JsonElement, JsonElement> requireAnalyzers)
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(scenario.Baseline);
        repo.Write(scenario.Counters, scenario.Edited);

        await using var session = await McpSession.StartAsync();
        var legacy = requireAnalyzers(await session.EvaluateAsync(repo.Root, baseline));
        Assert.Equal("complete", legacy.GetProperty("status").GetString());
        Assert.Equal(88, legacy.GetProperty("score").GetInt32());
        Assert.False(legacy.GetProperty("passed").GetBoolean());
        var finding = Assert.Single(Charged(legacy));
        Assert.Equal("cohesion.lcom4", finding.GetProperty("ruleId").GetString());
        Assert.Equal("pre_existing", finding.GetProperty("attribution").GetString());
        Assert.Equal(2, finding.GetProperty("actual").GetInt32());
        Assert.Equal(12, finding.GetProperty("deduction").GetInt32());
        Assert.Equal(scenario.Counters, finding.GetProperty("locations")[0].GetProperty("file").GetString()!.Replace('\\', '/'));

        repo.Write(scenario.Counters, scenario.Connected);
        var connected = await session.EvaluateAsync(repo.Root, baseline);
        Assert.Equal(100, connected.GetProperty("score").GetInt32());
        Assert.True(connected.GetProperty("passed").GetBoolean());
    }

    private static List<JsonElement> Charged(JsonElement response) =>
        response.GetProperty("findings").EnumerateArray().Where(f => f.GetProperty("deduction").GetInt32() > 0).ToList();

    internal static string ReasonCode(JsonElement response) =>
        response.GetProperty("reasons")[0].GetProperty("code").GetString()!;

    /// <summary>A repository whose <paramref name="Counters"/> class is split in the baseline, edited but still split, then connected.</summary>
    private sealed record CohesionScenario(string Counters, Dictionary<string, string> Baseline, string Edited, string Connected);
}
