using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Gradle module graph rules (cycle, boundary, main sequence) on statically read Kotlin DSL builds.</summary>
public sealed class KotlinModuleGraphTests
{
    private const string Settings = "rootProject.name = \"shop\"\ninclude(\":a\", \":b\")\n";

    private static string Build(params string[] dependencies) =>
        KotlinFixture.JvmBuild
        + (dependencies.Length == 0 ? string.Empty : "dependencies {\n" + string.Concat(dependencies.Select(d => $"    implementation(project(\":{d}\"))\n")) + "}\n");

    private static Dictionary<string, string> TwoModules(string? aTypes = null) => new()
    {
        ["settings.gradle.kts"] = Settings,
        ["a/build.gradle.kts"] = Build(),
        ["a/src/main/kotlin/a/Types.kt"] = aTypes ?? "package a\n\nclass AType {\n    fun v(): Int = 1\n}\n",
        ["b/build.gradle.kts"] = Build("a"),
        ["b/src/main/kotlin/b/BType.kt"] = "package b\n\nclass BType {\n    fun v(): Int = 2\n}\n",
    };

    private static string Types(int interfaces) =>
        "package a\n\n"
        + string.Concat(Enumerable.Range(0, 5).Select(i => i < interfaces ? $"interface I{i}\n" : $"class C{i}\n"))
        + string.Concat(Enumerable.Range(5, 5).Select(i => $"class K{i}\n"));

    private static JsonElement Evidence(JsonElement finding, string kind) =>
        finding.GetProperty("evidence").EnumerateArray().First(item => item.GetProperty("kind").GetString() == kind);

    [Fact]
    public async Task NewProjectDependencyCompletingACycleIsChargedFifteen()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(TwoModules());
        repo.Write("a/build.gradle.kts", Build("b"));

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        var finding = Assert.Single(KotlinFixture.Findings(response, "coupling.module-cycle"));
        Assert.Equal(15, finding.GetProperty("deduction").GetInt32());
        Assert.Equal("introduced", finding.GetProperty("attribution").GetString());
        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.Equal(85, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task NewForbiddenDependencyIsChargedButExistingOneIsContextOnly()
    {
        using var repo = new GitFixture();
        var files = TwoModules();
        files[".quality-architecture.json"] =
            "{\"version\":1,\"forbiddenDependencies\":[{\"from\":\"b/build.gradle.kts\",\"to\":\"a/build.gradle.kts\"}]}\n";
        var baseline = repo.Commit(files);
        repo.Write("b/src/main/kotlin/b/BType.kt", "package b\n\nclass BType {\n    fun v(): Int = 3\n}\n");
        var existing = await KotlinFixture.EvaluateAsync(repo, baseline);
        Assert.All(KotlinFixture.Findings(existing, "coupling.boundary"), f => Assert.Equal(0, f.GetProperty("deduction").GetInt32()));
        Assert.Equal(100, existing.GetProperty("score").GetInt32());

        var freshBaseline = repo.Commit(new Dictionary<string, string> { ["b/build.gradle.kts"] = Build() });
        repo.Write("b/build.gradle.kts", Build("a"));
        var introduced = await KotlinFixture.EvaluateAsync(repo, freshBaseline);

        var finding = Assert.Single(KotlinFixture.Findings(introduced, "coupling.boundary"));
        Assert.Equal(15, finding.GetProperty("deduction").GetInt32());
        Assert.Equal(85, introduced.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task MovingAModuleAwayFromTheMainSequenceIsChargedWithReproducibleEvidence()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(TwoModules(Types(5)));
        repo.Write("a/src/main/kotlin/a/Types.kt", Types(4));

        await using var session = await McpSession.StartAsync();
        var response = KotlinFixture.RequireToolchain(await session.EvaluateAsync(repo.Root, baseline));
        var details = await session.DetailsAsync(response.GetProperty("evaluationId").GetString()!, "coupling.main-sequence-distance");

        Assert.Equal(88, response.GetProperty("score").GetInt32());
        var finding = Assert.Single(details.GetProperty("findings").EnumerateArray());
        Assert.Equal("a/build.gradle.kts|main", KotlinFixture.Posix(finding.GetProperty("contextId").GetString()));
        var evidence = Evidence(finding, "mainSequence");
        Assert.Equal(10, evidence.GetProperty("eligibleTypes").GetInt32());
        Assert.Equal(4, evidence.GetProperty("abstractTypes").GetInt32());
        Assert.Equal(10, evidence.GetProperty("baselineEligibleTypes").GetInt32());
        Assert.Equal(5, evidence.GetProperty("baselineAbstractTypes").GetInt32());
        Assert.Equal(1, evidence.GetProperty("afferent").GetInt32());
        Assert.Equal(0, evidence.GetProperty("efferent").GetInt32());
        Assert.Equal(0.6, evidence.GetProperty("distance").GetDouble(), 12);
        Assert.Equal(0.5, evidence.GetProperty("baselineDistance").GetDouble(), 12);
    }

    [Fact]
    public async Task ChangingOnlyABuildScriptEvaluatesTheContextsItConfigures()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(TwoModules());
        repo.Write("b/build.gradle.kts", Build("a") + "kotlin { jvmToolchain(17) }\n");

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.Equal(100, response.GetProperty("score").GetInt32());
        Assert.Contains("b/build.gradle.kts", response.GetProperty("scope").EnumerateArray().Select(entry => KotlinFixture.Posix(entry.GetProperty("path").GetString())));
    }
}
