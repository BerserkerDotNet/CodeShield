using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeShield.CSharp.Metrics;

/// <summary>
/// Nesting depth: a construct's body sits one level deeper than the construct itself. The depth of a construct is
/// the number of enclosing constructs whose body part contains it (an <c>else if</c> is not inside its parent's body).
/// </summary>
internal static class NestingCounter
{
    private static readonly Dictionary<SyntaxKind, Func<SyntaxNode, SyntaxNode, bool>> BodyParts = new()
    {
        [SyntaxKind.IfStatement] = (parent, child) =>
            child == ((IfStatementSyntax)parent).Statement || child is ElseClauseSyntax { Statement: not IfStatementSyntax },
        [SyntaxKind.ForStatement] = (parent, child) => child == ((ForStatementSyntax)parent).Statement,
        [SyntaxKind.ForEachStatement] = (parent, child) => child == ((CommonForEachStatementSyntax)parent).Statement,
        [SyntaxKind.ForEachVariableStatement] = (parent, child) => child == ((CommonForEachStatementSyntax)parent).Statement,
        [SyntaxKind.WhileStatement] = (parent, child) => child == ((WhileStatementSyntax)parent).Statement,
        [SyntaxKind.DoStatement] = (parent, child) => child == ((DoStatementSyntax)parent).Statement,
        [SyntaxKind.SwitchStatement] = (_, child) => child is SwitchSectionSyntax,
        [SyntaxKind.SwitchExpression] = (_, child) => child is SwitchExpressionArmSyntax,
        [SyntaxKind.CatchClause] = (parent, child) => child == ((CatchClauseSyntax)parent).Block,
        [SyntaxKind.ConditionalExpression] = (parent, child) =>
            child == ((ConditionalExpressionSyntax)parent).WhenTrue || child == ((ConditionalExpressionSyntax)parent).WhenFalse,
    };

    public static (int Max, IReadOnlyList<NestingEvidence> Points) Measure(SyntaxNode unit)
    {
        var points = FunctionUnits.OwnNodes(unit)
            .Where(node => BodyParts.ContainsKey(node.Kind()))
            .Select(node => new NestingEvidence(Describe(node), SourceSpans.LineOf(node), Depth(node, unit) + 1))
            .ToList();
        return (points.Count == 0 ? 0 : points.Max(point => point.Depth), points);
    }

    private static string Describe(SyntaxNode node) => node.Kind() switch
    {
        SyntaxKind.ConditionalExpression => "?:",
        var kind => kind.ToString().Replace("Statement", "", StringComparison.Ordinal).Replace("Clause", "", StringComparison.Ordinal).ToLowerInvariant(),
    };

    private static int Depth(SyntaxNode node, SyntaxNode unit)
    {
        var depth = 0;
        var child = node;
        while (child != unit && child.Parent is { } parent)
        {
            depth += IsInBody(parent, child) ? 1 : 0;
            child = parent;
        }

        return depth;
    }

    private static bool IsInBody(SyntaxNode parent, SyntaxNode child) =>
        BodyParts.TryGetValue(parent.Kind(), out var test) && test(parent, child);
}
