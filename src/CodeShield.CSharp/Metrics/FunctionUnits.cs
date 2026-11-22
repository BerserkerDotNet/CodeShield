using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeShield.CSharp.Metrics;

/// <summary>Finds executable function bodies and enumerates the nodes that belong to exactly one of them.</summary>
internal static class FunctionUnits
{
    public static IEnumerable<SyntaxNode> Find(SyntaxNode root) => root.DescendantNodes().Where(IsUnit);

    public static bool IsUnit(SyntaxNode node) => node is AnonymousFunctionExpressionSyntax || IsBodied(node);

    private static bool IsBodied(SyntaxNode node) => node switch
    {
        BaseMethodDeclarationSyntax method => HasBody(method.Body, method.ExpressionBody),
        AccessorDeclarationSyntax accessor => HasBody(accessor.Body, accessor.ExpressionBody),
        PropertyDeclarationSyntax property => property.ExpressionBody is not null,
        IndexerDeclarationSyntax indexer => indexer.ExpressionBody is not null,
        LocalFunctionStatementSyntax local => HasBody(local.Body, local.ExpressionBody),
        _ => false,
    };

    private static bool HasBody(BlockSyntax? body, ArrowExpressionClauseSyntax? expression) =>
        body is not null || expression is not null;

    /// <summary>Nodes inside <paramref name="unit"/>, excluding nested functions and everything they contain.</summary>
    public static IEnumerable<SyntaxNode> OwnNodes(SyntaxNode unit) =>
        unit.DescendantNodes(node => node == unit || !UnitIds.IsNestedFunction(node))
            .Where(node => !UnitIds.IsNestedFunction(node));

    public static int ParameterCount(SyntaxNode unit)
    {
        if (unit is AccessorDeclarationSyntax accessor)
        {
            return accessor.Parent?.Parent is IndexerDeclarationSyntax indexer ? indexer.ParameterList.Parameters.Count : 0;
        }

        return unit.ChildNodes().OfType<BaseParameterListSyntax>().Sum(list => list.Parameters.Count)
            + unit.ChildNodes().OfType<ParameterSyntax>().Count();
    }
}
