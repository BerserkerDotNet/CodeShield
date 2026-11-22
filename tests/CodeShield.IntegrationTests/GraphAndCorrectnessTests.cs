using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Graph, boundary and correctness conformance through the real MCP server.</summary>
public sealed class GraphAndCorrectnessTests
{
    private const string Tfm = "net10.0";
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"},\"include\":[\"*.ts\"]}\n";

    private static string Project(string references = "") =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{Tfm}</TargetFramework></PropertyGroup>{references}</Project>\n";

    private static string Reference(string path) => $"<ItemGroup><ProjectReference Include=\"{path}\" /></ItemGroup>";

    private static async Task<JsonElement> EvaluateAsync(GitFixture repo, string baseline)
    {
        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    private static List<JsonElement> Findings(JsonElement response, string ruleIdPrefix) =>
        [.. response.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("ruleId").GetString()!.StartsWith(ruleIdPrefix, StringComparison.Ordinal))];

    private static Dictionary<string, string> TwoProjects() => new()
    {
        ["A/A.csproj"] = Project(),
        ["A/A.cs"] = "namespace A; public sealed class AType { public int V() => 1; }\n",
        ["B/B.csproj"] = Project(Reference("..\\A\\A.csproj")),
        ["B/B.cs"] = "namespace B; public sealed class BType { public int V() => new A.AType().V(); }\n",
    };

    [Fact]
    public async Task NewProjectReferenceCompletingACycleIsChargedFifteen()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(TwoProjects());
        repo.Write("A/A.csproj", Project(Reference("..\\B\\B.csproj")));

        var response = await EvaluateAsync(repo, baseline);

        var finding = Assert.Single(Findings(response, "coupling.module-cycle"));
        Assert.Equal(15, finding.GetProperty("deduction").GetInt32());
        Assert.Equal("introduced", finding.GetProperty("attribution").GetString());
    }

    [Fact]
    public async Task NewEdgeInsideAnExistingCycleIsStillCharged()
    {
        using var repo = new GitFixture();
        var files = TwoProjects();
        files["A/A.csproj"] = Project(Reference("..\\B\\B.csproj"));
        files["C/C.csproj"] = Project();
        files["C/C.cs"] = "namespace C; public sealed class CType { }\n";
        var baseline = repo.Commit(files);
        repo.Write("C/C.csproj", Project(Reference("..\\A\\A.csproj")));
        repo.Write("A/A.csproj", Project(Reference("..\\B\\B.csproj") + Reference("..\\C\\C.csproj")));

        var response = await EvaluateAsync(repo, baseline);

        Assert.Contains(Findings(response, "coupling.module-cycle"), f => f.GetProperty("deduction").GetInt32() == 15);
    }

    [Fact]
    public async Task ExistingCycleWithoutNewEdgeIsContextOnly()
    {
        using var repo = new GitFixture();
        var files = TwoProjects();
        files["A/A.csproj"] = Project(Reference("..\\B\\B.csproj"));
        var baseline = repo.Commit(files);
        repo.Write("B/B.cs", "namespace B; public sealed class BType { public int V() => new A.AType().V() + 1; }\n");

        var response = await EvaluateAsync(repo, baseline);

        Assert.All(Findings(response, "coupling.module-cycle"), f => Assert.Equal(0, f.GetProperty("deduction").GetInt32()));
    }

    [Fact]
    public async Task NewForbiddenDependencyIsChargedButExistingOneIsContextOnly()
    {
        using var repo = new GitFixture();
        var files = TwoProjects();
        files[".quality-architecture.json"] =
            "{\"version\":1,\"forbiddenDependencies\":[{\"from\":\"B\\\\B.csproj\",\"to\":\"A\\\\A.csproj\"}]}\n";
        var baseline = repo.Commit(files);
        repo.Write("B/B.cs", "namespace B; public sealed class BType { public int V() => new A.AType().V() + 2; }\n");
        var existing = await EvaluateAsync(repo, baseline);
        Assert.All(Findings(existing, "coupling.boundary"), f => Assert.Equal(0, f.GetProperty("deduction").GetInt32()));

        var freshBaseline = repo.Commit(new Dictionary<string, string> { ["B/B.csproj"] = Project(), ["B/B.cs"] = "namespace B; public sealed class BType { }\n" });
        repo.Write("B/B.csproj", Project(Reference("..\\A\\A.csproj")));
        var introduced = await EvaluateAsync(repo, freshBaseline);
        var finding = Assert.Single(Findings(introduced, "coupling.boundary"));
        Assert.Equal(15, finding.GetProperty("deduction").GetInt32());
    }

    [Fact]
    public async Task NewEdgeInsideAnExistingTypeScriptFileCycleIsCharged()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package,
            ["tsconfig.json"] = Tsconfig,
            ["a.ts"] = "import { b } from './b';\nexport const a: number = b + 1;\nexport const a2 = 1;\n",
            ["b.ts"] = "import { a2 } from './a';\nexport const b: number = a2 + 1;\n",
            ["c.ts"] = "export const c = 3;\n",
        });
        repo.Write("a.ts", "import { b } from './b';\nimport { c } from './c';\nexport const a: number = b + c;\nexport const a2 = 1;\n");
        repo.Write("c.ts", "import { a } from './a';\nexport const c: number = a + 1;\n");

        var response = await EvaluateAsync(repo, baseline);

        var charged = Findings(response, "coupling.typescript-file-cycle").Where(f => f.GetProperty("deduction").GetInt32() == 15).ToList();
        Assert.Single(charged);
    }

    [Theory]
    [InlineData("export async function work(): Promise<void> {}\nexport function run(): void {\n  work();\n}\n", true)]
    [InlineData("export async function work(): Promise<void> {}\nexport async function run(): Promise<void> {\n  await work();\n}\n", false)]
    [InlineData("export function pick(items: number[]): number[] {\n  return items.map((item) => { if (item > 1) { return item; } });\n}\n", true)]
    [InlineData("export function pick(items: number[]): number[] {\n  return items.map((item) => item * 2);\n}\n", false)]
    public async Task TypeScriptCorrectnessRuleTriggersAndDoesNotTrigger(string source, bool triggers)
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["package.json"] = Package, ["tsconfig.json"] = Tsconfig, ["a.ts"] = "export const a = 1;\n" });
        repo.Write("a.ts", source);

        var response = await EvaluateAsync(repo, baseline);

        var findings = Findings(response, "correctness.typescript.");
        Assert.Equal(triggers ? 1 : 0, findings.Count);
        Assert.Equal(triggers ? 88 : 100, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task OriginalDetailsStayRetrievableAcrossLaterEvaluationsOfTheSameSession()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["package.json"] = Package, ["tsconfig.json"] = Tsconfig, ["a.ts"] = "export const a = 1;\n" });
        repo.Write("a.ts", "export function f(a: number, b: number, c: number, d: number, e: number, g: number): number {\n  return a + b + c + d + e + g;\n}\n");
        await using var session = await McpSession.StartAsync();
        var first = await session.EvaluateAsync(repo.Root, baseline);
        repo.Write("a.ts", "export const a = 2;\n");
        var second = await session.EvaluateAsync(repo.Root, baseline);

        var details = await session.DetailsAsync(first.GetProperty("evaluationId").GetString()!, "readability.parameter-count");

        Assert.NotEqual(first.GetProperty("evaluationId").GetString(), second.GetProperty("evaluationId").GetString());
        Assert.Equal("complete", details.GetProperty("status").GetString());
        Assert.Single(details.GetProperty("findings").EnumerateArray());
    }
}
