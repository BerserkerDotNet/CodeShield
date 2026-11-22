using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Attribution labels of method findings on changed units, asserted through the real MCP server.</summary>
public sealed class AttributionTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Tsconfig = "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"},\"include\":[\"*.ts\"]}\n";

    private static string Function(string name, int branches, string extra = "")
    {
        var body = new StringBuilder($"export function {name}(x: number): number {{\n  let r = 0;\n{extra}");
        for (var i = 0; i < branches; i++)
        {
            body.Append(CultureInfo.InvariantCulture, $"  if (x === {i}) {{ r += {i}; }}\n");
        }

        return body.Append("  return r;\n}\n").ToString();
    }

    private static async Task<JsonElement> EvaluateChangeAsync(string baselineSource, string currentSource)
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package,
            ["tsconfig.json"] = Tsconfig,
            ["a.ts"] = baselineSource,
        });
        repo.Write("a.ts", currentSource);
        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    private static JsonElement Single(JsonElement response)
    {
        var findings = response.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("ruleId").GetString() == "complexity.cyclomatic").ToList();
        return Assert.Single(findings);
    }

    [Fact]
    public async Task ChangedUnitAlreadyOverTheLimitIsPreExistingButStillDeducted()
    {
        var response = await EvaluateChangeAsync(Function("f", 6), Function("f", 6, "  // touched\n"));

        var finding = Single(response);
        Assert.Equal("pre_existing", finding.GetProperty("attribution").GetString());
        Assert.Equal(12, finding.GetProperty("deduction").GetInt32());
        Assert.Equal(88, response.GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task ChangeThatCrossesTheLimitIsIntroduced()
    {
        var response = await EvaluateChangeAsync(Function("f", 5), Function("f", 6));

        Assert.Equal("introduced", Single(response).GetProperty("attribution").GetString());
    }

    [Fact]
    public async Task RenamedUnitWithUnmappableIdentityIsAmbiguousAndDeducted()
    {
        var response = await EvaluateChangeAsync(Function("original", 6), Function("renamed", 6));

        var finding = Single(response);
        Assert.Equal("ambiguous", finding.GetProperty("attribution").GetString());
        Assert.Equal(12, finding.GetProperty("deduction").GetInt32());
    }

    [Fact]
    public async Task GitRecognizedFileRenameKeepsUnitIdentityAsPreExisting()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["package.json"] = Package,
            ["tsconfig.json"] = Tsconfig,
            ["old.ts"] = Function("f", 6),
        });
        repo.Git("mv", "old.ts", "moved.ts");
        repo.Write("moved.ts", Function("f", 6, "  // touched\n"));
        repo.Stage("moved.ts");
        await using var session = await McpSession.StartAsync();

        var response = await session.EvaluateAsync(repo.Root, baseline);

        var finding = Single(response);
        Assert.Equal("pre_existing", finding.GetProperty("attribution").GetString());
        Assert.Equal(12, finding.GetProperty("deduction").GetInt32());
    }

    [Fact]
    public async Task NewMethodInExistingFileIsIntroduced()
    {
        var response = await EvaluateChangeAsync(Function("keep", 1), Function("keep", 1) + "\n" + Function("added", 6));

        Assert.Equal("introduced", Single(response).GetProperty("attribution").GetString());
    }
}
