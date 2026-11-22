using System.Text.Json;

namespace CodeShield.IntegrationTests;

/// <summary>Each selected C# correctness rule through the real MCP server: trigger and non-trigger with the fixed deduction.</summary>
public sealed class CSharpCorrectnessRuleTests
{
    private const string LoggingProject =
        "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>" +
        "<LangVersion>latest</LangVersion></PropertyGroup><ItemGroup>" +
        "<PackageReference Include=\"Microsoft.Extensions.Logging.Abstractions\" Version=\"9.0.0\" /></ItemGroup></Project>\n";

    private static string Source(string members) =>
        "using System;\nusing System.IO;\nusing System.Linq;\nusing System.Threading;\nusing System.Threading.Tasks;\n" +
        "using System.Collections.Generic;\nusing Microsoft.Extensions.Logging;\n" +
        $"namespace Fixture {{ public sealed class Subject {{\n{members}\n}} }}\n";

    public static TheoryData<string, string> Cases => new()
    {
        { "correctness.csharp.ca2000", "public void Run() { var s = new FileStream(\"x\", FileMode.Open); Console.WriteLine(s.Length); }" },
        { "correctness.csharp.ca2011", "public int P { get => 1; set { P = value; } }" },
        { "correctness.csharp.ca2013", "public bool Run(int a) => ReferenceEquals(this, a);" },
        { "correctness.csharp.ca2014", "public void Run() { for (var i = 0; i < 3; i++) { Span<byte> s = stackalloc byte[8]; s[0] = 1; } }" },
        { "correctness.csharp.ca2016", "public async Task Run(CancellationToken token) { await Task.Delay(1); token.ThrowIfCancellationRequested(); }" },
        { "correctness.csharp.ca2018", "public void Run(int[] a, int[] b) { Buffer.BlockCopy(a, 0, b, 0, a.Length); }" },
        { "correctness.csharp.ca2019", "[ThreadStatic] private static int value = 5;\npublic int Run() => value;" },
        { "correctness.csharp.ca2021", "public IEnumerable<string> Run(int[] a) => a.Cast<string>();" },
        { "correctness.csharp.ca2022", "public void Run(Stream s, byte[] buffer) { s.Read(buffer, 0, buffer.Length); }" },
        { "correctness.csharp.ca2017", "public void Run(ILogger log) { log.LogInformation(\"{A} {B}\", 1); }" },
        { "correctness.csharp.ca2023", "public void Run(ILogger log) { log.LogInformation(\"{A\", 1); }" },
        { "correctness.csharp.ca2012", "public ValueTask<int> Get() => new(1);\npublic int Run() { var t = Get(); return t.Result + t.Result; }" },
    };

    private static async Task<JsonElement> EvaluateAsync(string members)
    {
        using var repo = new GitFixture();
        var baseline = repo.Commit(new Dictionary<string, string>
        {
            [".gitignore"] = "bin/\nobj/\n",
            ["Subject.csproj"] = LoggingProject,
            ["Subject.cs"] = Source("public int Ok() => 1;"),
        });
        repo.Restore("Subject.csproj");
        repo.Write("Subject.cs", Source("public int Ok() => 1;\n" + members));
        await using var session = await McpSession.StartAsync();
        return await session.EvaluateAsync(repo.Root, baseline);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EachSelectedRuleTriggersAndChargesTheFixedDeduction(string ruleId, string members)
    {
        var response = await EvaluateAsync(members);

        Assert.True(response.GetProperty("status").GetString() == "complete", response.GetProperty("reasons").ToString());
        var finding = Assert.Single(response.GetProperty("findings").EnumerateArray(), f => f.GetProperty("ruleId").GetString() == ruleId);
        Assert.Equal(12, finding.GetProperty("deduction").GetInt32());
        Assert.Contains(response.GetProperty("coverage").EnumerateArray(), c => c.GetProperty("ruleId").GetString() == ruleId);
    }

    [Fact]
    public async Task CleanEquivalentsOfTheTriggersProduceNoCorrectnessFindings()
    {
        var response = await EvaluateAsync(
            "public int P { get; set; }\npublic bool Run(object a, object b) => ReferenceEquals(a, b);\n" +
            "public async Task Wait(CancellationToken token) { await Task.Delay(1, token); }\n" +
            "public void Read(Stream s, byte[] buffer) { s.ReadExactly(buffer); }\n" +
            "public void Log(ILogger log) { log.LogInformation(\"{A} {B}\", 1, 2); }");

        Assert.Equal("complete", response.GetProperty("status").GetString());
        var correctness = response.GetProperty("findings").EnumerateArray()
            .Where(f => f.GetProperty("ruleId").GetString()!.StartsWith("correctness.", StringComparison.Ordinal)).ToList();
        Assert.Empty(correctness);
    }
}
