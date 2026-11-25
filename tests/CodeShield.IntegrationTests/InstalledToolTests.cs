using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Runs only when CODESHIELD_INSTALLED_EXE points at an installed executable; skipped otherwise.</summary>
public sealed class InstalledFactAttribute : FactAttribute
{
    public const string Variable = "CODESHIELD_INSTALLED_EXE";

    public InstalledFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Set {Variable} to an installed codeshield executable to run installed-package tests.";
        }
    }
}

/// <summary>The packaged tool, started as `codeshield mcp` from an unrelated directory, must behave like the source build.</summary>
public sealed class InstalledToolTests
{
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\"},\"include\":[\"**/*.ts\"]}\n";
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";

    private static Task<McpSession> StartAsync()
    {
        var unrelated = Path.Combine(Path.GetTempPath(), "cs installed cwd");
        Directory.CreateDirectory(unrelated);
        return McpSession.StartInstalledAsync(Environment.GetEnvironmentVariable(InstalledFactAttribute.Variable)!, unrelated, "mcp");
    }

    [InstalledFact]
    public async Task ExposesTheTwoToolsAndEvaluatesTypeScriptWithThePackagedWorker()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package,
            ["tsconfig.json"] = Tsconfig,
            ["api.ts"] = "export const revision = 1;\n",
        });
        repo.Write("api.ts", "export function h(a: number, b: number, c: number, d: number, e: number, f: number) { return a + b + c + d + e + f; }\n");

        await using var session = await StartAsync();
        var response = await session.EvaluateAsync(repo.Root, baseline);

        Assert.Equal(["evaluate_quality", "get_rule_details"], await session.ToolNamesAsync());
        Assert.Equal("complete", response.GetProperty("status").GetString());
        Assert.True(response.GetProperty("score").GetInt32() < 100);
        Assert.Contains(response.GetProperty("findings").EnumerateArray(),
            finding => finding.GetProperty("ruleId").GetString() == "readability.parameter-count");
    }

    [InstalledFact]
    public async Task EvaluatesCSharpWithThePackagedAnalyzers()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["App.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Library</OutputType></PropertyGroup></Project>\n",
            ["Api.cs"] = "public static class Api { public static int Revision => 1; }\n",
        });
        repo.Write("Api.cs", "public static class Api { public static int Revision => 2; }\n");

        await using var session = await StartAsync();
        var response = await session.EvaluateAsync(repo.Root, baseline);

        Assert.Equal(JsonValueKind.Number, response.GetProperty("score").ValueKind);
        Assert.NotEqual("error", response.GetProperty("status").GetString());
    }
}
