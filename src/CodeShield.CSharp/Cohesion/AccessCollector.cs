using CodeShield.CSharp.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeShield.CSharp.Cohesion;

internal sealed record AccessContext(SemanticModel Model, IReadOnlySet<ISymbol> State, IReadOnlyDictionary<ISymbol, CohesionMember> Nodes);

/// <summary>State touched and nodes called by one member body (nested functions excluded).</summary>
internal sealed record MemberAccesses(IReadOnlyList<ISymbol> State, IReadOnlyList<CohesionMember> Calls);

internal static class AccessCollector
{
    public static MemberAccesses Collect(CohesionMember member, AccessContext context)
    {
        var state = new List<ISymbol>();
        var calls = new List<CohesionMember>();
        foreach (var name in FunctionUnits.OwnNodes(member.Syntax).OfType<SimpleNameSyntax>())
        {
            var symbol = (context.Model.GetSymbolInfo(name).Symbol ?? context.Model.GetSymbolInfo(name).CandidateSymbols.FirstOrDefault())?.OriginalDefinition;
            if (symbol is not null && context.State.Contains(symbol))
            {
                state.Add(symbol);
            }
            else if (symbol is not null)
            {
                calls.AddRange(Callees(symbol, name, context));
            }
        }

        return new MemberAccesses(state, calls);
    }

    private static IEnumerable<CohesionMember> Callees(ISymbol symbol, SimpleNameSyntax name, AccessContext context)
    {
        var targets = symbol switch
        {
            IMethodSymbol method => [method],
            IPropertySymbol property => Accessors(property, Usage(name)),
            _ => Array.Empty<IMethodSymbol>(),
        };
        return targets
            .Select(target => context.Nodes.GetValueOrDefault(target.OriginalDefinition))
            .OfType<CohesionMember>();
    }

    private static IMethodSymbol[] Accessors(IPropertySymbol property, (bool Read, bool Write) usage) =>
        [.. new[] { usage.Read ? property.GetMethod : null, usage.Write ? property.SetMethod : null }.OfType<IMethodSymbol>()];

    private static ExpressionSyntax AccessExpression(SimpleNameSyntax name) =>
        name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;

    private static (bool Read, bool Write) Usage(SimpleNameSyntax name)
    {
        var expression = AccessExpression(name);
        return expression.Parent switch
        {
            AssignmentExpressionSyntax assignment when assignment.Left == expression =>
                (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression), true),
            PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression } => (true, true),
            PostfixUnaryExpressionSyntax => (true, true),
            ArgumentSyntax { RefKindKeyword.RawKind: (int)SyntaxKind.RefKeyword or (int)SyntaxKind.OutKeyword } => (true, true),
            _ => (true, false),
        };
    }
}
