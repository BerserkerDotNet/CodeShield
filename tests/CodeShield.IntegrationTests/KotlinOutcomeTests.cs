using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Kotlin incomplete, error and mixed-language outcomes through the real MCP server.</summary>
public sealed class KotlinOutcomeTests
{
    private static void AssertNoScore(JsonElement response, string status, string code)
    {
        Assert.Equal(status, response.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, response.GetProperty("score").ValueKind);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("passed").ValueKind);
        Assert.Contains(code, KotlinFixture.Reasons(response).Select(r => r.GetProperty("code").GetString()));
    }

    private static (GitFixture Repo, string Baseline) ChangedSingleProject()
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        repo.Write("src/main/kotlin/app/Seed.kt", "package app\n\nval seed = 2\n");
        return (repo, baseline);
    }

    [Theory]
    [InlineData("CODESHIELD_DETEKT", "detekt-cli")]
    [InlineData("CODESHIELD_KOTLIN_WORKER", "Kotlin worker")]
    [InlineData("CODESHIELD_JAVA", "Java")]
    public async Task AMissingPrerequisiteMakesKotlinEvaluationIncompleteWithMissingAnalyzer(string variable, string mentioned)
    {
        var (repo, baseline) = ChangedSingleProject();
        using var _ = repo;
        var missing = Path.Combine(repo.Root, "not-installed", "missing.jar");
        await using var session = await McpSession.StartAsync(new Dictionary<string, string> { [variable] = missing });

        var response = await session.EvaluateAsync(repo.Root, baseline);

        AssertNoScore(response, "incomplete", "MISSING_ANALYZER");
        var reason = KotlinFixture.Reasons(response).First(r => r.GetProperty("code").GetString() == "MISSING_ANALYZER");
        Assert.Contains(mentioned, reason.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task KotlinFileOutsideEveryGradleBuildIsAnUnresolvedOwnership()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["README.md"] = "x\n", ["Loose.kt"] = "class Loose\n" });
        repo.Write("Loose.kt", "class Loose {\n    val v = 1\n}\n");

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        AssertNoScore(response, "incomplete", "UNRESOLVED_OWNERSHIP");
    }

    [Fact]
    public async Task SuppressingASelectedKotlinRuleMakesTheEvaluationIncomplete()
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        using var _ = repo;
        repo.Write("src/main/kotlin/app/Seed.kt", "package app\n\n@Suppress(\"InvalidRange\")\nfun countDown() {\n    for (i in 10..1) println(i)\n}\n");

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        AssertNoScore(response, "incomplete", "SUPPRESSED_RULE");
        var reason = KotlinFixture.Reasons(response).First(r => r.GetProperty("code").GetString() == "SUPPRESSED_RULE");
        Assert.Equal("src/main/kotlin/app/Seed.kt:3 disables InvalidRange, so the rule cannot be assessed.", reason.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UnparseableBuildScriptIsAProjectLoadFailure()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["settings.gradle.kts"] = "include(\":app\")\n",
            ["app/build.gradle.kts"] = KotlinFixture.JvmBuild,
            ["app/src/main/kotlin/A.kt"] = "class A\n",
        });
        repo.Write("app/build.gradle.kts", "dependencies { implementation(project(\":core\") }\n");
        repo.Write("app/src/main/kotlin/A.kt", "class A {\n    val v = 1\n}\n");

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        Assert.Equal(JsonValueKind.Null, response.GetProperty("score").ValueKind);
        Assert.NotEqual("complete", response.GetProperty("status").GetString());
        Assert.Contains("PROJECT_LOAD_FAILED", KotlinFixture.Reasons(response).Select(r => r.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task KotlinSyntaxErrorMakesTheSourceUnreadable()
    {
        var (repo, baseline) = KotlinFixture.SingleProject();
        using var _ = repo;
        repo.Write("src/main/kotlin/app/Seed.kt", "package app\n\nfun broken( {\n");

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        Assert.Equal(JsonValueKind.Null, response.GetProperty("score").ValueKind);
        Assert.NotEqual("complete", response.GetProperty("status").GetString());
        Assert.Contains("UNREADABLE_SOURCE", KotlinFixture.Reasons(response).Select(r => r.GetProperty("code").GetString()));
    }

    [Fact]
    public async Task OneEvaluationScoresCSharpAndKotlinChangesTogether()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["Api/Api.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup></Project>\n",
            ["Api/Api.cs"] = "namespace Api;\n\npublic static class Calculator\n{\n    public static int Revision => 1;\n}\n",
            ["mobile/build.gradle.kts"] = KotlinFixture.JvmBuild,
            ["mobile/src/main/kotlin/app/Calculator.kt"] = "package app\n\nval revision = 1\n",
        });
        repo.Write("Api/Api.cs", "namespace Api;\n\npublic static class Calculator\n{\n    public static int Sum(int a, int b, int c, int d, int e, int f) => a + b + c + d + e + f;\n}\n");
        repo.Write("mobile/src/main/kotlin/app/Calculator.kt", "package app\n\nfun sum(a: Int, b: Int, c: Int, d: Int, e: Int, f: Int): Int = a + b + c + d + e + f\n");

        var response = await KotlinFixture.EvaluateAsync(repo, baseline);

        var files = KotlinFixture.Findings(response, "readability.parameter-count")
            .Select(KotlinFixture.FileOf)
            .Order(StringComparer.Ordinal);
        Assert.Equal(["Api/Api.cs", "mobile/src/main/kotlin/app/Calculator.kt"], files);
        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.Equal(90, response.GetProperty("score").GetInt32());
    }
}
