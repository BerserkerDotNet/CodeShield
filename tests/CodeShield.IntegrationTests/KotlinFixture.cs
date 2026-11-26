using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>
/// Shared Gradle fixtures for the Kotlin integration tests. They need JDK 17+, the built worker jar and detekt-cli 1.23.x
/// (on PATH or through CODESHIELD_DETEKT); a missing prerequisite fails the test with instructions instead of a bare assertion.
/// </summary>
internal static class KotlinFixture
{
    public const string JvmBuild = "plugins { kotlin(\"jvm\") }\n";

    private const string Prerequisites =
        "The Kotlin integration tests need JDK 17+, the Kotlin worker jar (scripts/setup builds kotlin-worker/dist) and detekt-cli 1.23.x "
        + "on PATH or in CODESHIELD_DETEKT (scripts/fetch-detekt.ps1 downloads it). CodeShield reported: ";

    /// <summary>A committed single-project Gradle build with one Kotlin source file, plus any extra files.</summary>
    public static (GitFixture Repo, string Baseline) SingleProject(IReadOnlyDictionary<string, string>? extra = null)
    {
        var repo = new GitFixture();
        var files = new Dictionary<string, string>
        {
            ["build.gradle.kts"] = JvmBuild,
            ["src/main/kotlin/app/Seed.kt"] = "package app\n\nval seed = 1\n",
        };
        foreach (var (path, text) in extra ?? new Dictionary<string, string>())
        {
            files[path] = text;
        }

        return (repo, repo.Commit(files));
    }

    /// <summary>Evaluates in a fresh server and fails with setup instructions when the Kotlin toolchain is not available.</summary>
    public static async Task<JsonElement> EvaluateAsync(GitFixture repo, string baseline)
    {
        await using var session = await McpSession.StartAsync();
        return RequireToolchain(await session.EvaluateAsync(repo.Root, baseline));
    }

    public static JsonElement RequireToolchain(JsonElement response)
    {
        var missing = Reasons(response).FirstOrDefault(reason => reason.GetProperty("code").GetString() == "MISSING_ANALYZER");
        if (missing.ValueKind != JsonValueKind.Undefined)
        {
            Assert.Fail(Prerequisites + missing.GetProperty("message").GetString());
        }

        return response;
    }

    /// <summary>Top-level reasons and per-context coverage reasons.</summary>
    public static IEnumerable<JsonElement> Reasons(JsonElement response) =>
        response.GetProperty("reasons").EnumerateArray()
            .Concat(response.GetProperty("coverage").EnumerateArray().SelectMany(entry => entry.GetProperty("reasons").EnumerateArray()));

    /// <summary>Responses carry OS-native paths; tests compare them in repository (forward-slash) form.</summary>
    public static string Posix(string? path) => path!.Replace('\\', '/');

    public static string FileOf(JsonElement finding) => Posix(finding.GetProperty("locations")[0].GetProperty("file").GetString());

    public static List<JsonElement> Findings(JsonElement response, string ruleIdPrefix) =>
        [.. response.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("ruleId").GetString()!.StartsWith(ruleIdPrefix, StringComparison.Ordinal))];
}
