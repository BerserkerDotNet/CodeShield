using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Advertised TypeScript source kinds (.tsx/.mts/.cts) and allowJs contexts through the real MCP server.</summary>
public sealed class SourceKindTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";
    private const string Wide = "export function wide(a: number, b: number, c: number, d: number, e: number, f: number): number { return a + b + c + d + e + f; }\n";

    private static string Config(bool allowJs) =>
        "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"," +
        $"\"jsx\":\"preserve\",\"allowJs\":{(allowJs ? "true" : "false")}}},\"include\":[\"*.ts\",\"*.tsx\",\"*.mts\",\"*.cts\",\"*.js\"]}}\n";

    private static async Task<JsonElement> EvaluateAsync(Dictionary<string, string> baselineFiles, Dictionary<string, string> edits)
    {
        using var repo = new GitFixture();
        var files = new Dictionary<string, string>(baselineFiles) { ["package.json"] = Package };
        var baseline = repo.Commit(files);
        foreach (var edit in edits)
        {
            repo.Write(edit.Key, edit.Value);
        }

        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    [Theory]
    [InlineData("view.tsx")]
    [InlineData("module.mts")]
    [InlineData("common.cts")]
    public async Task AdvertisedSourceExtensionsAreAnalyzed(string file)
    {
        var response = await EvaluateAsync(
            new Dictionary<string, string> { ["tsconfig.json"] = Config(false), ["keep.ts"] = "export const keep = 1;\n" },
            new Dictionary<string, string> { [file] = Wide });

        Assert.Equal("complete", response.GetProperty("status").GetString());
        var finding = Assert.Single(response.GetProperty("findings").EnumerateArray());
        Assert.Equal("readability.parameter-count", finding.GetProperty("ruleId").GetString());
        Assert.Equal(file, finding.GetProperty("locations")[0].GetProperty("file").GetString());
        Assert.Equal(95, response.GetProperty("score").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AllowJsContextsStillResolveTypeScriptDependencies(bool allowJs)
    {
        var response = await EvaluateAsync(
            new Dictionary<string, string>
            {
                ["tsconfig.json"] = Config(allowJs),
                ["a.ts"] = "export const a = 1;\n",
                ["b.ts"] = "export const b = 2;\n",
                ["legacy.js"] = "export const legacy = 3;\n",
            },
            new Dictionary<string, string>
            {
                ["a.ts"] = "import { b } from './b';\nexport const a = b + 1;\n",
                ["b.ts"] = "import { a } from './a';\nexport const b = a + 1;\n",
            });

        Assert.Equal("complete", response.GetProperty("status").GetString());
        var cycle = Assert.Single(response.GetProperty("findings").EnumerateArray());
        Assert.Equal("coupling.typescript-file-cycle", cycle.GetProperty("ruleId").GetString());
        Assert.Equal(85, response.GetProperty("score").GetInt32());
    }
}
