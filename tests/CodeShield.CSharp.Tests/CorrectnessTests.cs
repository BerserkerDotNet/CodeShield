namespace CodeShield.CSharp.Tests;

public sealed class CorrectnessTests
{
    private const string Logging =
        "namespace Microsoft.Extensions.Logging { public interface ILogger { }\n" +
        "public static class LoggerMessage { }\n" +
        "public static class LoggerExtensions { public static void LogInformation(this ILogger logger, string? message, params object?[] args) { } } }\n";

    public static TheoryData<string, string, string> Cases => new()
    {
        { "CA2000", "public void M() { var s = new FileStream(\"a\", FileMode.Open); s.ReadByte(); }",
                    "public void M() { using var s = new FileStream(\"a\", FileMode.Open); s.ReadByte(); }" },
        { "CA2011", "int p; public int P { get => p; set { P = value; } }",
                    "int p; public int P { get => p; set { p = value; } }" },
        { "CA2012", "static ValueTask<int> Get() => new(1); public void M() { Get(); }",
                    "static ValueTask<int> Get() => new(1); public async Task M() { await Get(); }" },
        { "CA2013", "public bool M() => ReferenceEquals(1, 2);",
                    "public bool M() => ReferenceEquals(new object(), null);" },
        { "CA2014", "public void M() { for (var i = 0; i < 3; i++) { Span<int> s = stackalloc int[4]; s[0] = i; } }",
                    "public void M() { Span<int> s = stackalloc int[4]; for (var i = 0; i < 3; i++) { s[0] = i; } }" },
        { "CA2016", "public async Task M(CancellationToken token) { await Task.Delay(1); }",
                    "public async Task M(CancellationToken token) { await Task.Delay(1, token); }" },
        { "CA2017", "public void M(Microsoft.Extensions.Logging.ILogger l) { Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(l, \"{a} {b}\", 1); }",
                    "public void M(Microsoft.Extensions.Logging.ILogger l) { Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(l, \"{a} {b}\", 1, 2); }" },
        { "CA2018", "public void M(int[] src, int[] dst) { Buffer.BlockCopy(src, 0, dst, 0, src.Length); }",
                    "public void M(int[] src, int[] dst) { Buffer.BlockCopy(src, 0, dst, 0, src.Length * sizeof(int)); }" },
        { "CA2019", "[ThreadStatic] static int x = 5; public int M() => x;",
                    "[ThreadStatic] static int x; public int M() => x;" },
        { "CA2021", "public object M() => new List<string>().Cast<int>();",
                    "public object M() => new List<object>().Cast<int>();" },
        { "CA2022", "public void M(Stream s, byte[] b) { s.Read(b, 0, 1); }",
                    "public int M(Stream s, byte[] b) { return s.Read(b, 0, 1); }" },
        { "CA2023", "public void M(Microsoft.Extensions.Logging.ILogger l) { Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(l, \"{a\", 1); }",
                    "public void M(Microsoft.Extensions.Logging.ILogger l) { Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(l, \"{a}\", 1); }" },
    };

    private static async Task<CodeShield.Abstractions.ContextFacts> RunAsync(string member)
    {
        using var fixture = new Fixture();
        fixture.Project().Write("A.cs", $"public class A {{ {member} }}\n{Logging}");
        return await fixture.SingleAsync("A.cs");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Rule_triggers_on_bad_code_and_not_on_good_code(string id, string bad, string good)
    {
        var flagged = await RunAsync(bad);
        Assert.Empty(flagged.Failures);
        Assert.Contains(flagged.Diagnostics, d => d.AnalyzerId == id);
        var hit = flagged.Diagnostics.First(d => d.AnalyzerId == id);
        Assert.Equal("A.cs", hit.File);
        Assert.StartsWith("A.cs#A.", hit.UnitId, StringComparison.Ordinal);

        var clean = await RunAsync(good);
        Assert.Empty(clean.Failures);
        Assert.DoesNotContain(clean.Diagnostics, d => d.AnalyzerId == id);
    }

    [Fact]
    public async Task Only_selected_rules_are_reported()
    {
        var context = await RunAsync("public void M() { var unused = 1; string s = null!; }");
        Assert.Empty(context.Diagnostics);
    }

    [Fact]
    public async Task Diagnostics_in_generated_files_are_not_reported()
    {
        using var fixture = new Fixture();
        fixture.Project().Write("A.cs", "public class A { }")
            .Write("B.g.cs", "public class B { public bool M() => ReferenceEquals(1, 2); }");
        var context = await fixture.SingleAsync("A.cs");
        Assert.Empty(context.Diagnostics);
    }

    [Theory]
    [InlineData("#pragma warning disable CA2013\npublic class A { public bool M() => ReferenceEquals(1, 2); }", "correctness.csharp.ca2013")]
    [InlineData("#pragma warning disable\npublic class A { }", "correctness.csharp.ca2000")]
    [InlineData("[System.Diagnostics.CodeAnalysis.SuppressMessage(\"Reliability\", \"CA2000:Dispose\")]\npublic class A { }", "correctness.csharp.ca2000")]
    public async Task Suppressions_fail_the_rule_instead_of_passing(string source, string ruleId)
    {
        using var fixture = new Fixture();
        fixture.Project().Write("A.cs", source);
        var context = await fixture.SingleAsync("A.cs");
        Assert.True(context.Fails(ruleId, "SUPPRESSED_RULE"));
    }

    [Fact]
    public async Task Unrelated_pragma_is_not_a_suppression()
    {
        using var fixture = new Fixture();
        fixture.Project().Write("A.cs", "#pragma warning disable CS0168\npublic class A { }");
        var context = await fixture.SingleAsync("A.cs");
        Assert.Empty(context.Failures);
    }
}
