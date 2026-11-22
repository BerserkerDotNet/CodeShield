using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Baseline dependency handling with a real restored package, FrameworkReference and netstandard target.</summary>
public sealed class RestoredDependencyTests
{
    private const string Project =
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks>" +
        "<LangVersion>latest</LangVersion><NoWarn>NETSDK1086</NoWarn></PropertyGroup>" +
        "<ItemGroup><PackageReference Include=\"Microsoft.Extensions.Logging.Abstractions\" Version=\"9.0.0\" /></ItemGroup>" +
        "<ItemGroup Condition=\"'$(TargetFramework)'=='net10.0'\"><FrameworkReference Include=\"Microsoft.NETCore.App\" /></ItemGroup></Project>\n";

    private const string Before =
        "using Microsoft.Extensions.Logging;\nnamespace Lib { public sealed class Worker { private readonly ILogger _log;\n" +
        "public Worker(ILogger log) { _log = log; }\npublic void Run() { _log.LogInformation(\"run\"); } } }\n";

    private const string After =
        "using Microsoft.Extensions.Logging;\nnamespace Lib { public sealed class Worker { private readonly ILogger _log;\n" +
        "public Worker(ILogger log) { _log = log; }\npublic void Run() { _log.LogInformation(\"run\"); }\n" +
        "public int Sum(int a, int b, int c, int d, int e, int f) { _log.LogInformation(\"sum\"); return a + b + c + d + e + f; } } }\n";

    private static string Fingerprint(string root) => string.Join('\n',
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => $"{path}|{new FileInfo(path).Length}|{File.GetLastWriteTimeUtc(path).Ticks}"));

    [Fact]
    public async Task RestoredPackageFrameworkReferenceAndNetStandardBaselineAreAnalyzedWithoutMutation()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            [".gitignore"] = "bin/\nobj/\n",
            ["Lib.csproj"] = Project,
            ["Worker.cs"] = Before,
        });
        repo.Restore("Lib.csproj");
        repo.Write("Worker.cs", After);
        var before = Fingerprint(repo.Root);
        var status = repo.Git("status", "--porcelain");

        await using var session = await McpSession.StartAsync();
        var response = await session.EvaluateAsync(repo.Root, baseline);

        Assert.True(response.GetProperty("status").GetString() == "complete", response.GetProperty("reasons").ToString());
        Assert.Empty(response.GetProperty("reasons").EnumerateArray());
        var findings = response.GetProperty("findings").EnumerateArray().ToList();
        Assert.Equal(["Lib.csproj|net10.0", "Lib.csproj|netstandard2.1"],
            findings.Select(f => f.GetProperty("contextId").GetString()!).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(findings, f => Assert.Equal("readability.parameter-count", f.GetProperty("ruleId").GetString()));
        Assert.Equal(90, response.GetProperty("score").GetInt32());
        Assert.Equal(before, Fingerprint(repo.Root));
        Assert.Equal(status, repo.Git("status", "--porcelain"));
        Assert.False(Directory.Exists(Path.Combine(repo.Root, "bin")));
    }

    [Fact]
    public async Task AnUnrestoredProjectReportsMissingDependencyInsteadOfRestoring()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            [".gitignore"] = "bin/\nobj/\n", ["Lib.csproj"] = Project, ["Worker.cs"] = Before,
        });
        repo.Write("Worker.cs", After);

        await using var session = await McpSession.StartAsync();
        var response = await session.EvaluateAsync(repo.Root, baseline);

        Assert.NotEqual("complete", response.GetProperty("status").GetString());
        Assert.Contains("MISSING_DEPENDENCY", AcceptanceTests.ReasonCode(response), StringComparison.Ordinal);
        Assert.Null(response.GetProperty("score").GetInt32OrNull());
        Assert.False(Directory.Exists(Path.Combine(repo.Root, "obj")));
    }
}

internal static class JsonElementExtensions
{
    public static int? GetInt32OrNull(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Null ? null : element.GetInt32();
}
