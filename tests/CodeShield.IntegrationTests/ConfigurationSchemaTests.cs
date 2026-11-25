using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace CodeShield.IntegrationTests;

/// <summary>The .codeshield.json schema and the response's configuration block must reject malformed documents.</summary>
public sealed class ConfigurationSchemaTests
{
    private static readonly JsonSchema ConfigSchema = Read("codeshield-config.v1.schema.json");

    private static JsonSchema Read(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(directory.FullName, "docs", "schemas")))
        {
            directory = directory.Parent ?? throw new DirectoryNotFoundException("docs\\schemas not found.");
        }

        return JsonSchema.FromText(File.ReadAllText(Path.Combine(directory.FullName, "docs", "schemas", name)));
    }

    private static bool IsValid(string json) =>
        ConfigSchema.Evaluate(JsonDocument.Parse(json).RootElement).IsValid;

    [Theory]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"exclude\":true,\"reason\":\"generated\"}")]
    [InlineData("{\"version\":1,\"rules\":{\"complexity.cyclomatic\":{\"limit\":2147483647,\"reason\":\"legacy\"}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"coupling.main-sequence-distance\":{\"limit\":0.7,\"reason\":\"kernel\"}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"complexity.nesting\":{\"limit\":0}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"readability.parameter-count\":{\"limit\":5}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"cohesion.lcom4\":{\"limit\":1}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"coupling.main-sequence-distance\":{\"limit\":1,\"reason\":\"r\"}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"structure.duplication\":{\"enabled\":false,\"reason\":\"r\"}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"correctness.csharp.ca2000\":{\"enabled\":false,\"reason\":\"r\"}}}")]
    public void ValidDocumentsAreAccepted(string json) => Assert.True(IsValid(json));

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"version\":2}")]
    [InlineData("{\"version\":1,\"exclude\":true}")]
    [InlineData("{\"version\":1,\"exclude\":true,\"reason\":\"  \"}")]
    [InlineData("{\"version\":1,\"unknown\":1}")]
    [InlineData("{\"version\":1,\"rules\":{\"complexity.cyclomatic\":{\"limit\":2147483648,\"reason\":\"x\"}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"complexity.cyclomatic\":{\"enabled\":false}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"no.such-rule\":{\"enabled\":true}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"complexity.nesting\":{\"limit\":0.5,\"reason\":\"r\"}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"complexity.cyclomatic\":{\"limit\":0}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"coupling.main-sequence-distance\":{\"limit\":2,\"reason\":\"r\"}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"readability.parameter-count\":{\"limit\":6}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"readability.parameter-count\":{\"limit\":6,\"reason\":\" \"}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"cohesion.lcom4\":{\"limit\":2}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"structure.file-length\":{\"limit\":401}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"coupling.main-sequence-distance\":{\"limit\":0.6}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"structure.duplication\":{\"limit\":3}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"coupling.boundary\":{\"limit\":1,\"reason\":\"r\"}}}")]
    [InlineData("{\"version\":1,\"rules\":{\"correctness.csharp.not-a-pinned-rule\":{\"enabled\":false,\"reason\":\"r\"}}}")]
    public void MalformedDocumentsAreRejected(string json) => Assert.False(IsValid(json));

    [Fact]
    public void KnownRuleIdsMatchTheReleaseManifest()
    {
        var names = ConfigSchemaText().GetProperty("properties").GetProperty("rules").GetProperty("propertyNames").GetProperty("enum")
            .EnumerateArray().Select(item => item.GetString()!).Order(StringComparer.Ordinal).ToList();
        var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(DocsDirectory(), "release-manifest.json"))).RootElement
            .GetProperty("rules").EnumerateArray().Select(rule => rule.GetProperty("id").GetString()!).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(manifest, names);
    }

    private static JsonElement ConfigSchemaText() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(DocsDirectory(), "schemas", "codeshield-config.v1.schema.json"))).RootElement;

    private static string DocsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!Directory.Exists(Path.Combine(directory.FullName, "docs", "schemas")))
        {
            directory = directory.Parent ?? throw new DirectoryNotFoundException("docs not found.");
        }

        return Path.Combine(directory.FullName, "docs");
    }

    [Fact]
    public async Task RealConfigurationBlockValidatesAndBrokenVariantsDoNot()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["package.json"] = "{\"name\":\"f\",\"version\":\"1.0.0\",\"private\":true}\n", ["a.ts"] = "export const a = 1;\n" });
        repo.Write(".codeshield.json", "{\"version\":1,\"exclude\":true,\"reason\":\"vendored\"}\n");
        await using var session = await McpSession.StartAsync();
        var response = JsonNode.Parse((await session.EvaluateAsync(repo.Root, baseline)).GetRawText())!;

        Assert.True(PublishedSchemas.EvaluateIsValid(response));
        response["configuration"]!.AsObject().Remove("changed");
        Assert.False(PublishedSchemas.EvaluateIsValid(response));
        response["configuration"]!["changed"] = true;
        response["configuration"]!["files"]![0]!["change"] = "bogus";
        Assert.False(PublishedSchemas.EvaluateIsValid(response));
    }
}
