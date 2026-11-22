using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Full-detail evidence must carry the data that reproduces each calculation, with consistent identifiers.</summary>
public sealed class EvidenceContractTests
{
    private const string Tfm = "net10.0";

    private static string Project(string references = "") =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>{Tfm}</TargetFramework></PropertyGroup>{references}</Project>\n";

    private static string Types(int interfaces) =>
        string.Concat(Enumerable.Range(0, 5).Select(i => i < interfaces ? $"public interface I{i} {{ }}\n" : $"public class C{i} {{ }}\n")) +
        string.Concat(Enumerable.Range(5, 5).Select(i => $"public class K{i} {{ }}\n"));

    private static JsonElement Evidence(JsonElement finding, string kind) =>
        finding.GetProperty("evidence").EnumerateArray().First(item => item.GetProperty("kind").GetString() == kind);

    [Fact]
    public async Task DistanceDetailsCarryTheCountsBehindTheCalculation()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["Subject/Subject.csproj"] = Project(),
            ["Subject/Types.cs"] = Types(5),
            ["Consumer/Consumer.csproj"] = Project("<ItemGroup><ProjectReference Include=\"..\\Subject\\Subject.csproj\" /></ItemGroup>"),
            ["Consumer/Use.cs"] = "public class Use { public int V() => 1; }\n",
        });
        repo.Write("Subject/Types.cs", Types(4));

        await using var session = await McpSession.StartAsync();
        var response = await session.EvaluateAsync(repo.Root, baseline);
        var details = await session.DetailsAsync(response.GetProperty("evaluationId").GetString()!, "coupling.main-sequence-distance");

        Assert.Equal(88, response.GetProperty("score").GetInt32());
        var evidence = Evidence(details.GetProperty("findings")[0], "mainSequence");
        Assert.Equal(10, evidence.GetProperty("eligibleTypes").GetInt32());
        Assert.Equal(4, evidence.GetProperty("abstractTypes").GetInt32());
        Assert.Equal(10, evidence.GetProperty("baselineEligibleTypes").GetInt32());
        Assert.Equal(5, evidence.GetProperty("baselineAbstractTypes").GetInt32());
        Assert.Equal(1, evidence.GetProperty("afferent").GetInt32());
        Assert.Equal(0, evidence.GetProperty("efferent").GetInt32());
        Assert.Equal(1, evidence.GetProperty("baselineAfferent").GetInt32());
        Assert.Equal(0.6, evidence.GetProperty("distance").GetDouble(), 12);
        Assert.Equal(0.5, evidence.GetProperty("baselineDistance").GetDouble(), 12);
    }

    [Fact]
    public async Task CohesionNodesEdgesAndComponentsShareStableIdentifiers()
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            ["Fixture.csproj"] = Project(),
            ["Counters.cs"] = "public sealed partial class Counters\n{\n    private int left;\n    private int right;\n    public int Next(int by) => left += by;\n    public int NextRight() => ++right;\n}\n",
        });
        repo.Write("Counters.cs", "public sealed partial class Counters\n{\n    private int left;\n    private int right;\n    public int Next(int by) => left += by;\n    public int Next(string by) => left += by.Length;\n    public int NextRight() => ++right;\n}\n");
        repo.Write("Counters.More.cs", "public sealed partial class Counters\n{\n    public int RightTwice() => right += 2;\n}\n");

        await using var session = await McpSession.StartAsync();
        var response = await session.EvaluateAsync(repo.Root, baseline);
        var details = await session.DetailsAsync(response.GetProperty("evaluationId").GetString()!, "cohesion.lcom4");

        var finding = details.GetProperty("findings")[0];
        var nodeIds = Evidence(finding, "nodes").GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("id").GetString()!).ToList();
        var members = Evidence(finding, "components").GetProperty("groups").EnumerateArray().SelectMany(g => g.EnumerateArray().Select(m => m.GetString()!)).ToList();
        var edgeEnds = Evidence(finding, "edges").GetProperty("edges").EnumerateArray()
            .SelectMany(e => new[] { e.GetProperty("from").GetString()!, e.GetProperty("to").GetString()! });

        Assert.Equal(4, nodeIds.Count);
        Assert.Equal(nodeIds.Distinct(StringComparer.Ordinal).Count(), nodeIds.Count);
        Assert.Equal(nodeIds.Order(StringComparer.Ordinal), members.Order(StringComparer.Ordinal));
        Assert.All(edgeEnds, end => Assert.Contains(end, nodeIds));
    }
}
