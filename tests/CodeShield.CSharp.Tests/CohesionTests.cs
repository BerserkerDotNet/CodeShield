using CodeShield.Abstractions;

namespace CodeShield.CSharp.Tests;

public sealed class CohesionTests
{
    private static async Task<ClassFact> ClassOfAsync(string source, string className = "A")
    {
        using var fixture = new Fixture();
        fixture.Project().Write("A.cs", source);
        var context = await fixture.SingleAsync("A.cs");
        return context.Classes.Single(c => c.Name == className);
    }

    [Fact]
    public async Task Two_independent_groups_give_two_components()
    {
        var fact = await ClassOfAsync("public class A { int a; int b; public int X() => a; public int Y() => b; }");
        Assert.True(fact.Applicable);
        Assert.Equal(2, fact.Components.Count);
        Assert.All(fact.Edges, edge => Assert.StartsWith("field:", edge.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Shared_field_gives_one_component()
    {
        var fact = await ClassOfAsync("public class A { int a; public int X() => a; public void Y() { a = 1; } }");
        Assert.Single(fact.Components);
    }

    [Fact]
    public async Task Direct_call_connects_methods()
    {
        var fact = await ClassOfAsync("public class A { int a; int b; public int X() => a + Y(); public int Y() => b; }");
        Assert.Single(fact.Components);
        Assert.Contains(fact.Edges, edge => edge.Reason == "call");
    }

    [Fact]
    public async Task Stateless_class_is_not_applicable()
    {
        var fact = await ClassOfAsync("public class A { public int X() => 1; public int Y() => 2; }");
        Assert.False(fact.Applicable);
    }

    [Fact]
    public async Task Static_members_constructors_and_inherited_state_are_excluded()
    {
        var fact = await ClassOfAsync(
            "public class B { protected int inherited; }\n" +
            "public class A : B { static int s; int a; public A() { a = 1; }\n" +
            "public int X() => a; public int Y() => inherited; public static int Z() => s; }");
        Assert.Equal(2, fact.Nodes.Count);
        Assert.Equal(2, fact.Components.Count);
    }

    [Fact]
    public async Task Partial_classes_are_merged_across_declarations()
    {
        using var fixture = new Fixture();
        fixture.Project()
            .Write("A1.cs", "public partial class A { int a; public int X() => a; }")
            .Write("A2.cs", "public partial class A { public void Y() { a = 2; } }");
        var context = await fixture.SingleAsync("A1.cs");
        var fact = Assert.Single(context.Classes);
        Assert.Single(fact.Components);
        Assert.Equal(2, fact.Locations.Count);
    }

    [Fact]
    public async Task Property_accessors_are_nodes_and_resolve_through_properties()
    {
        var fact = await ClassOfAsync(
            "public class A { int a; int b;\n" +
            "public int P { get { return a; } set { a = value; } }\n" +
            "public int Q { get { return b; } }\n" +
            "public int Use() => P; }");
        Assert.Equal(2, fact.Components.Count);
        Assert.Contains(fact.Nodes, node => node.Id.EndsWith("P.get", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Auto_property_counts_as_state()
    {
        var fact = await ClassOfAsync("public class A { int Left { get; set; } int Right { get; set; } public int X() => Left; public int Y() => Right; }");
        Assert.Equal(2, fact.Components.Count);
    }

    [Fact]
    public async Task Lambda_bodies_do_not_connect_methods()
    {
        var fact = await ClassOfAsync(
            "public class A { int a; int b; public Func<int> X() => () => a; public Func<int> Y() => () => b; }");
        Assert.Equal(2, fact.Components.Count);
    }

    [Fact]
    public async Task Types_are_reported_for_all_owned_non_generated_files()
    {
        using var fixture = new Fixture();
        fixture.Project()
            .Write("A.cs", "public interface I {} public abstract class B { public class N {} } public struct S {}")
            .Write("G.g.cs", "public class Gen {}");
        var context = await fixture.SingleAsync("A.cs");
        Assert.Contains(context.Types, t => t.UnitId.EndsWith("I", StringComparison.Ordinal) && t.IsAbstract);
        Assert.Contains(context.Types, t => t.UnitId.EndsWith("B", StringComparison.Ordinal) && t.IsAbstract);
        Assert.Contains(context.Types, t => t.UnitId.EndsWith("B.N", StringComparison.Ordinal) && !t.IsAbstract);
        Assert.Contains(context.Types, t => t.UnitId.EndsWith("S", StringComparison.Ordinal));
        Assert.DoesNotContain(context.Types, t => t.File == "G.g.cs");
    }
}
