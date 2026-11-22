using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeShield.IntegrationTests;

/// <summary>Real responses must satisfy the published schemas, and semantically broken responses must not.</summary>
public sealed class SchemaConformanceTests
{
    private const string Package = "{\"name\":\"fixture\",\"version\":\"1.0.0\",\"private\":true}\n";

    private const string Tsconfig =
        "{\"compilerOptions\":{\"strict\":true,\"noEmit\":true,\"target\":\"ES2022\",\"module\":\"ESNext\",\"moduleResolution\":\"Bundler\"},\"include\":[\"*.ts\"]}\n";

    private static string Branchy() =>
        "export function f(n: number): number {\n  let r = 0;\n" +
        string.Concat(Enumerable.Range(0, 7).Select(i => $"  if (n === {i}) {{ r += {i}; }}\n")) +
        "  return r;\n}\n";

    private static async Task<(JsonNode Evaluation, JsonNode Details)> CaptureAsync()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string> { ["package.json"] = Package, ["tsconfig.json"] = Tsconfig, ["a.ts"] = "export const a = 1;\n" });
        repo.Write("a.ts", Branchy());
        await using var session = await McpSession.StartAsync();
        var evaluation = await session.EvaluateAsync(repo.Root, baseline);
        var details = await session.DetailsAsync(evaluation.GetProperty("evaluationId").GetString()!, "complexity.cyclomatic");
        return (JsonNode.Parse(evaluation.GetRawText())!, JsonNode.Parse(details.GetRawText())!);
    }

    private static async Task<JsonNode> ErrorAsync()
    {
        await using var session = await McpSession.StartAsync();
        var response = await session.EvaluateAsync(Path.GetTempPath(), "HEAD", contractVersion: "2");
        return JsonNode.Parse(response.GetRawText())!;
    }

    [Fact]
    public async Task RealResponsesValidateAndCarryTheExpectedShape()
    {
        var (evaluation, details) = await CaptureAsync();

        Assert.Equal("complete", (string?)evaluation["status"]);
        Assert.True(PublishedSchemas.EvaluateIsValid(evaluation));
        Assert.True(PublishedSchemas.DetailsIsValid(details));
        Assert.True(PublishedSchemas.EvaluateIsValid(await ErrorAsync()));
    }

    [Theory]
    [InlineData("score")]
    [InlineData("passed")]
    [InlineData("evaluationId")]
    [InlineData("baselineCommit")]
    [InlineData("snapshotId")]
    [InlineData("rulesetVersion")]
    [InlineData("coverage")]
    public async Task EvaluateRejectsOmittedRequiredFields(string field)
    {
        var (evaluation, _) = await CaptureAsync();
        evaluation.AsObject().Remove(field);

        Assert.False(PublishedSchemas.EvaluateIsValid(evaluation));
    }

    [Fact]
    public async Task EvaluateRejectsInvalidStatusScoreCombinations()
    {
        var (evaluation, _) = await CaptureAsync();

        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(evaluation, node => node["score"] = null)));
        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(evaluation, node => node["passed"] = null)));
        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(evaluation, node => node["passed"] = !(bool)node["passed"]!)));
        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(evaluation, node => node["status"] = "incomplete")));
        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(await ErrorAsync(), node => node["score"] = 100)));
        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(await ErrorAsync(), node => node["passed"] = true)));
    }

    [Fact]
    public async Task EvaluateRejectsAFindingWithoutEvidence()
    {
        var (evaluation, _) = await CaptureAsync();

        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(evaluation, node => node["findings"]![0]!["evidence"] = new JsonArray())));
        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(evaluation, node => node["findings"]![0]!.AsObject().Remove("deduction"))));
    }

    [Fact]
    public async Task DetailsRejectsErasedOrIncompleteCalculationEvidence()
    {
        var (_, details) = await CaptureAsync();

        Assert.False(PublishedSchemas.DetailsIsValid(Mutate(details, node => node["findings"]![0]!["evidence"] = new JsonArray())));
        Assert.False(PublishedSchemas.DetailsIsValid(Mutate(details, RemoveKind("decisionCount"))));
        Assert.False(PublishedSchemas.DetailsIsValid(Mutate(details, RemoveKind("method"))));
        Assert.False(PublishedSchemas.DetailsIsValid(Mutate(details, node => node["findings"]![0]!.AsObject().Remove("calculation"))));
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("intent")]
    [InlineData("ruleId")]
    [InlineData("contextId")]
    [InlineData("actual")]
    [InlineData("limit")]
    public async Task EvaluateRejectsAFindingMissingAContractKey(string key)
    {
        var (evaluation, _) = await CaptureAsync();

        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(evaluation, node => node["findings"]![0]!.AsObject().Remove(key))));
    }

    [Theory]
    [InlineData("ruleId")]
    [InlineData("contextId")]
    public async Task EvaluateRejectsAReasonMissingANullableKey(string key)
    {
        var error = await ErrorAsync();

        Assert.True(PublishedSchemas.EvaluateIsValid(error));
        Assert.False(PublishedSchemas.EvaluateIsValid(Mutate(error, node => node["reasons"]![0]!.AsObject().Remove(key))));
    }

    [Theory]
    [InlineData("actual")]
    [InlineData("limit")]
    public async Task DetailsRejectsAFindingMissingActualOrLimit(string key)
    {
        var (_, details) = await CaptureAsync();

        Assert.False(PublishedSchemas.DetailsIsValid(Mutate(details, node => node["findings"]![0]!.AsObject().Remove(key))));
    }

    [Theory]
    [InlineData("ruleId")]
    [InlineData("rulesetVersion")]
    [InlineData("definition")]
    [InlineData("deductionPerFinding")]
    public async Task DetailsRejectsOmittedRequiredFields(string field)
    {
        var (_, details) = await CaptureAsync();
        details.AsObject().Remove(field);

        Assert.False(PublishedSchemas.DetailsIsValid(details));
    }

    private static Action<JsonNode> RemoveKind(string kind) => node =>
    {
        var evidence = node["findings"]![0]!["evidence"]!.AsArray();
        var match = evidence.First(item => (string?)item!["kind"] == kind);
        evidence.Remove(match);
    };

    private static JsonNode Mutate(JsonNode original, Action<JsonNode> change)
    {
        var copy = JsonNode.Parse(original.ToJsonString(new JsonSerializerOptions()))!;
        change(copy);
        return copy;
    }
}
