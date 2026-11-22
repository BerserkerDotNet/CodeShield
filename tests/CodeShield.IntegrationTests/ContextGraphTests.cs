using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Per-context dependency graphs through the real MCP server: no phantom cycles, separate charges per context.</summary>
public sealed class ContextGraphTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";

    private static string Config(string aDep, string bDep) =>
        "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"," +
        $"\"paths\":{{\"@a-dep\":[\"./{aDep}.ts\"],\"@b-dep\":[\"./{bDep}.ts\"]}}}},\"include\":[\"a.ts\",\"b.ts\",\"leaf.ts\"]}}\n";

    private static async Task<JsonElement> EvaluateTypeScriptAsync(string first, string second)
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package,
            ["tsconfig.json"] = Config(first.Split(',')[0], first.Split(',')[1]),
            ["tsconfig.alternate.json"] = Config(second.Split(',')[0], second.Split(',')[1]),
            ["a.ts"] = "export const a = 1;\n",
            ["b.ts"] = "export const b = 2;\n",
            ["leaf.ts"] = "export const leaf = 3;\n",
        });
        repo.Write("a.ts", "import '@a-dep';\nexport const a = 1;\n");
        repo.Write("b.ts", "import '@b-dep';\nexport const b = 2;\n");
        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    private static List<JsonElement> Cycles(JsonElement response) =>
        [.. response.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("ruleId").GetString() == "coupling.typescript-file-cycle")];

    [Fact]
    public async Task EdgesThatNeverCoexistDoNotFormAPhantomCycle()
    {
        var response = await EvaluateTypeScriptAsync("b,leaf", "leaf,a");

        Assert.Empty(Cycles(response));
        Assert.Equal(100, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task AnIssueInOneOwningConfigIsChargedOnlyOnThatConfig()
    {
        var response = await EvaluateTypeScriptAsync("b,a", "leaf,a");

        var cycle = Assert.Single(Cycles(response));
        Assert.Equal("tsconfig.json", cycle.GetProperty("contextId").GetString());
        Assert.Equal(15, cycle.GetProperty("deduction").GetInt32());
        Assert.Equal(85, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task TheSameIssueInBothOwningConfigsIsChargedSeparately()
    {
        var response = await EvaluateTypeScriptAsync("b,a", "b,a");

        var cycles = Cycles(response);
        Assert.Equal(["tsconfig.alternate.json", "tsconfig.json"],
            cycles.Select(c => c.GetProperty("contextId").GetString()!).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(cycles, c => Assert.Equal(15, c.GetProperty("deduction").GetInt32()));
        Assert.Equal(70, response.GetProperty("score").GetInt32());
    }

    private static string MultiTarget(string conditionalReference) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>net10.0;netstandard2.1</TargetFrameworks>" +
        $"<LangVersion>latest</LangVersion></PropertyGroup>{conditionalReference}</Project>\n";

    private static string ConditionalReference(string path, string framework) =>
        $"<ItemGroup Condition=\"'$(TargetFramework)'=='{framework}'\"><ProjectReference Include=\"{path}\" /></ItemGroup>";

    private static Dictionary<string, string> MultiTargetFiles(string aReference, string bReference) => new()
    {
        ["A/A.csproj"] = MultiTarget(aReference),
        ["A/A.cs"] = "namespace A { public sealed class AType { public int V() => 1; } }\n",
        ["B/B.csproj"] = MultiTarget(bReference),
        ["B/B.cs"] = "namespace B { public sealed class BType { public int V() => 2; } }\n",
    };

    private static async Task<JsonElement> EvaluateMultiTargetAsync(string aReference, string bReference)
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(MultiTargetFiles(string.Empty, string.Empty));
        repo.Write("A/A.csproj", MultiTarget(aReference));
        repo.Write("B/B.csproj", MultiTarget(bReference));
        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    private static List<JsonElement> ModuleCycles(JsonElement response) =>
        [.. response.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("ruleId").GetString() == "coupling.module-cycle")];

    [Fact]
    public async Task ConditionalProjectReferencesOfDifferentFrameworksDoNotFormAPhantomCycle()
    {
        var response = await EvaluateMultiTargetAsync(
            ConditionalReference("..\\B\\B.csproj", "net10.0"), ConditionalReference("..\\A\\A.csproj", "netstandard2.1"));

        Assert.Empty(ModuleCycles(response));
        Assert.Equal("complete", response.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ACycleThatExistsInOneFrameworkIsChargedOnThatFramework()
    {
        var response = await EvaluateMultiTargetAsync(
            ConditionalReference("..\\B\\B.csproj", "net10.0"), ConditionalReference("..\\A\\A.csproj", "net10.0"));

        var cycle = Assert.Single(ModuleCycles(response));
        Assert.Equal("net10.0", cycle.GetProperty("contextId").GetString());
        Assert.Equal(15, cycle.GetProperty("deduction").GetInt32());
    }

    [Fact]
    public async Task TheSameCycleInBothFrameworksIsChargedPerFramework()
    {
        var response = await EvaluateMultiTargetAsync(
            Both("..\\B\\B.csproj"), Both("..\\A\\A.csproj"));

        var cycles = ModuleCycles(response);
        Assert.Equal(["net10.0", "netstandard2.1"], cycles.Select(c => c.GetProperty("contextId").GetString()!).OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(cycles, c => Assert.Equal(15, c.GetProperty("deduction").GetInt32()));
    }

    private static string Both(string path) => $"<ItemGroup><ProjectReference Include=\"{path}\" /></ItemGroup>";
}
