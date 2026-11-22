using CodeShield.Abstractions;

namespace CodeShield.CSharp.Tests;

public sealed class AcceptanceVectorTests
{
    private const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n";

    private const string Counters =
        "public sealed class Counters\n{\n    private int left;\n    private int right;\n    public int NextLeft() => ++left;\n    public int NextRight() => ++right;\n}\n";

    private const string Other =
        "public sealed class Other\n{\n    private int total;\n    private string text = \"\";\n    public void Reset() => total = 0;\n    public string Label() => text;\n}\n";

    [Fact]
    public async Task Counters_has_two_components_then_one_after_Total_is_added()
    {
        using var fixture = new Fixture();
        fixture.Write("Fixture.csproj", Project).Write("Counters.cs", Counters).Write("Other.cs", Other);

        var before = await fixture.SingleAsync("Counters.cs");
        var classFact = Assert.Single(before.Classes);
        Assert.Equal("Counters", classFact.Name);
        Assert.True(classFact.Applicable);
        Assert.Equal(2, classFact.Components.Count);
        Assert.Empty(before.Failures);
        Assert.Empty(before.Diagnostics);

        fixture.Write("Counters.cs", Counters.Replace("}\n", "    public int Total() => left + right;\n}\n"));
        var after = await fixture.SingleAsync("Counters.cs");
        var fixedClass = Assert.Single(after.Classes);
        Assert.Single(fixedClass.Components);
        Assert.Equal(["NextLeft()", "NextRight()", "Total()"], fixedClass.Components[0]);
        Assert.Empty(after.Failures);
        Assert.Empty(after.Diagnostics);
    }

    [Fact]
    public async Task Reports_context_identity_versions_and_inputs()
    {
        using var fixture = new Fixture();
        fixture.Write("Fixture.csproj", Project).Write("Counters.cs", Counters);

        var analysis = await fixture.AnalyzeAsync(["Counters.cs"]);

        var context = Assert.Single(analysis.Contexts);
        Assert.Equal("Fixture.csproj|net10.0", context.ContextId);
        Assert.Equal("Fixture.csproj", context.ConfigPath);
        Assert.Equal("net10.0", context.Framework);
        Assert.Equal(["Counters.cs"], context.OwnedFiles);
        Assert.All(["roslyn", "net-analyzers", "msbuild", "dotnet-sdk"], key => Assert.True(analysis.AnalyzerVersions.ContainsKey(key)));
        Assert.Contains(analysis.Inputs, input => input.Scope == InputScopes.Repository && input.Path == "Counters.cs");
        Assert.Contains(analysis.Inputs, input => input.Scope == InputScopes.External && input.Path.Contains("System.Runtime.dll"));
        Assert.All(analysis.Inputs.Where(input => input.Scope == InputScopes.External), input => Assert.DoesNotContain('\\', input.Path));
    }
}
