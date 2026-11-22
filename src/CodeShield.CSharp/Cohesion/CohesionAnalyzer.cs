using CodeShield.Abstractions;
using CodeShield.CSharp.Metrics;
using Microsoft.CodeAnalysis;

namespace CodeShield.CSharp.Cohesion;

/// <summary>LCOM4 for one class symbol, merged across its partial declarations.</summary>
internal static class CohesionAnalyzer
{
    public static ClassFact? Analyze(INamedTypeSymbol type, IReadOnlyDictionary<SyntaxTree, string> repoPaths, SemanticModels models)
    {
        var declarations = TypeDeclarations.Of(type, repoPaths);
        if (declarations.Count == 0)
        {
            return null;
        }

        var members = CohesionMembers.Nodes(type, models);
        var state = CohesionMembers.State(type);
        var nodes = members.Select(member => new CohesionNode(member.Id, member.Name)).ToList();
        var edges = Edges(members, state, models);
        return new ClassFact(
            TypeDeclarations.UnitId(declarations[0]),
            type.Name,
            [.. declarations.Select(declaration => new ClassLocation(declaration.File, SourceSpans.Of(declaration.Syntax)))],
            state.Count > 0 && nodes.Count >= 2,
            nodes,
            edges,
            ComponentGraph.Components(nodes, edges));
    }

    private static List<CohesionEdge> Edges(IReadOnlyList<CohesionMember> members, HashSet<ISymbol> state, SemanticModels models)
    {
        var lookup = members.ToDictionary<CohesionMember, ISymbol>(member => member.Symbol.OriginalDefinition, SymbolEqualityComparer.Default);
        var accesses = members.Select(member => (Member: member, Access: AccessCollector.Collect(member, ContextOf(member, state, lookup, models)))).ToList();
        var edges = accesses.SelectMany(item => item.Access.Calls.Where(callee => callee != item.Member).Select(callee => new CohesionEdge(item.Member.Id, callee.Id, "call")));
        return [.. edges.Concat(FieldEdges(accesses)).Distinct().OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal).ThenBy(edge => edge.Reason, StringComparer.Ordinal)];
    }

    private static AccessContext ContextOf(CohesionMember member, HashSet<ISymbol> state, Dictionary<ISymbol, CohesionMember> lookup, SemanticModels models) =>
        new(models.For(member.Syntax.SyntaxTree), state, lookup);

    private static IEnumerable<CohesionEdge> FieldEdges(List<(CohesionMember Member, MemberAccesses Access)> accesses) =>
        accesses
            .SelectMany(item => item.Access.State.Distinct(SymbolEqualityComparer.Default).Select(symbol => (Symbol: symbol, item.Member)))
            .GroupBy(item => item.Symbol, SymbolEqualityComparer.Default)
            .SelectMany(group => Star(group.Select(item => item.Member.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(), "field:" + group.Key!.Name));

    private static IEnumerable<CohesionEdge> Star(List<string> ids, string reason) =>
        ids.Skip(1).Select(id => new CohesionEdge(ids[0], id, reason));
}
