using System.Text;

namespace CodeShield.CSharp.Tests;

public sealed class MetricTests
{
    private sealed record Run(CodeShield.Abstractions.ContextFacts Context);

    private static async Task<Run> RunAsync(Fixture fixture, string source)
    {
        fixture.Project().Write("A.cs", source);
        return new Run(await fixture.SingleAsync("A.cs"));
    }
    private static string Ifs(int count) =>
        "public class A { public int M(int x) { var r = 0;\n" +
        string.Concat(Enumerable.Range(0, count).Select(i => $"if (x == {i}) r++;\n")) + "return r; } }";

    [Theory]
    [InlineData(5, 6)]
    [InlineData(6, 7)]
    public async Task Cyclomatic_complexity_counts_each_decision(int ifs, int expected)
    {
        using var fixture = new Fixture();
        var run = await RunAsync(fixture, Ifs(ifs));
        Assert.Equal(expected, run.Context.Method("A.M(int)").Complexity);
    }

    [Fact]
    public async Task Complexity_operators_catch_switch_and_default_rules()
    {
        using var fixture = new Fixture();
        var run = await RunAsync(fixture, 
            "public class A { public int M(int x, int? y, bool b) {\n" +
            "try { } catch (Exception) { }\n" +
            "var z = y ?? 1; var t = b && x > 1 || x < 0 ? 1 : 2;\n" +
            "switch (x) { case 1: return 1; case 2: return 2; default: return 0; } } }");
        // 1 + catch + ?? + && + || + ?: + two case labels
        Assert.Equal(8, run.Context.Method("A.M(int,int?,bool)").Complexity);
    }

    [Fact]
    public async Task Switch_expression_arms_count_and_discard_adds_nothing()
    {
        using var fixture = new Fixture();
        var run = await RunAsync(fixture, "public class A { public int M(int x) => x switch { 1 => 1, 2 => 2, _ => 0 }; }");
        Assert.Equal(3, run.Context.Method("A.M(int)").Complexity);
    }

    [Theory]
    [InlineData("if (x > 0) { if (x > 1) { x++; } }", 2)]
    [InlineData("if (x > 0) { if (x > 1) { if (x > 2) { x++; } } }", 3)]
    [InlineData("if (x > 0) { x++; } else if (x > 1) { x--; } else if (x > 2) { x = 0; } else { x = 5; }", 1)]
    [InlineData("do { if (x > 1) { x--; } } while (x > 0);", 2)]
    [InlineData("do { if (x > 1) { while (x > 0) { x--; } } } while (x > 5);", 3)]
    [InlineData("switch (x) { case 1: if (x > 1) { x--; } break; default: break; }", 2)]
    [InlineData("x = x > 1 ? (x > 2 ? 1 : 2) : 3;", 2)]
    public async Task Nesting_depth_follows_the_spec(string body, int expected)
    {
        using var fixture = new Fixture();
        var run = await RunAsync(fixture, $"public class A {{ public int M(int x) {{ {body} return x; }} }}");
        Assert.Equal(expected, run.Context.Method("A.M(int)").Nesting);
    }

    [Fact]
    public async Task Nested_lambdas_and_local_functions_are_separate_units()
    {
        using var fixture = new Fixture();
        var run = await RunAsync(fixture, 
            "public class A { public int M(int x) {\n" +
            "int Local(int y) { if (y > 0) { if (y > 1) { return 1; } } return 0; }\n" +
            "Func<int, int> f = v => v > 1 && v > 2 ? 1 : 0;\n" +
            "return x > 0 ? Local(x) : f(x); } }");
        var outer = run.Context.Method("A.M(int)");
        Assert.Equal(2, outer.Complexity);
        Assert.Equal(1, outer.Nesting);
        Assert.Equal(3, run.Context.Method("/Local(int)").Complexity);
        Assert.Equal(2, run.Context.Method("/Local(int)").Nesting);
        var lambda = run.Context.Files.SelectMany(file => file.Methods).Single(method => method.UnitId.Contains("<lambda@"));
        Assert.Equal(1, lambda.ParameterCount);
        Assert.Equal(3, lambda.Complexity);
        Assert.StartsWith(outer.UnitId, lambda.UnitId, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(36, 40)]
    [InlineData(37, 41)]
    public async Task Method_length_counts_code_lines_not_comments_or_blanks(int statements, int expected)
    {
        var body = new StringBuilder();
        for (var i = 0; i < statements; i++)
        {
            body.Append("x++;\n// comment\n\n");
        }

        using var fixture = new Fixture();
        var run = await RunAsync(fixture, $"public class A {{\npublic int M(int x)\n{{\n{body}return x;\n}}\n}}");
        Assert.Equal(expected, run.Context.Method("A.M(int)").CodeLines);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public async Task Parameter_count_is_the_declared_count(int count)
    {
        var parameters = string.Join(", ", Enumerable.Range(0, count).Select(i => $"int p{i}"));
        using var fixture = new Fixture();
        var run = await RunAsync(fixture, $"public class A {{ public void M({parameters}) {{ }} }}");
        Assert.Equal(count, run.Context.Files.Single().Methods.Single().ParameterCount);
    }

    [Fact]
    public async Task File_code_lines_ignore_comments_and_blank_lines()
    {
        using var fixture = new Fixture();
        var run = await RunAsync(fixture, "// header\n\npublic class A\n{\n    /* c */\n    public int X;\n}\n");
        Assert.Equal(4, run.Context.Files.Single().CodeLines);
    }

    [Fact]
    public async Task Multiline_tokens_count_every_line()
    {
        using var fixture = new Fixture();
        var run = await RunAsync(fixture, "public class A { public string M() {\nreturn \"\"\"\nline\nline\n\"\"\";\n} }");
        Assert.Equal(6, run.Context.Method("A.M()").CodeLines);
    }

    [Fact]
    public async Task Accessors_constructors_and_operators_are_units()
    {
        using var fixture = new Fixture();
        var run = await RunAsync(fixture, 
            "public class A { int f; public A() { f = 1; } ~A() { f = 0; }\n" +
            "public int P { get { return f; } set { f = value; } }\n" +
            "public int Q => f;\n" +
            "public static A operator +(A a, A b) { return a; } }");
        var ids = run.Context.Files.Single().Methods.Select(method => method.UnitId).ToList();
        Assert.Contains(ids, id => id.EndsWith(".ctor()", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.EndsWith("A.P.get", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.EndsWith("A.P.set", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.EndsWith("A.Q.get", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.Contains("operator+", StringComparison.Ordinal));
        Assert.Contains(ids, id => id.Contains("~A", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unit_ids_are_stable_when_lines_move()
    {
        using var firstFixture = new Fixture();
        var first = await RunAsync(firstFixture, "public class A { public int M(int x) { return x; } }");
        using var secondFixture = new Fixture();
        var second = await RunAsync(secondFixture, "\n\n\npublic class A {\n\n public int M(int x) { return x; } }");
        Assert.Equal(first.Context.Files.Single().Methods[0].UnitId, second.Context.Files.Single().Methods[0].UnitId);
    }
}
