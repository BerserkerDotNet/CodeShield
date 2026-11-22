using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeShield.CSharp.Metrics;

/// <summary>Cyclomatic complexity: 1 plus one per counted decision in the function's own body.</summary>
internal static class ComplexityCounter
{
    private static readonly Dictionary<SyntaxKind, string> Decisions = new()
    {
        [SyntaxKind.IfStatement] = "if",
        [SyntaxKind.ForStatement] = "for",
        [SyntaxKind.ForEachStatement] = "foreach",
        [SyntaxKind.ForEachVariableStatement] = "foreach",
        [SyntaxKind.WhileStatement] = "while",
        [SyntaxKind.DoStatement] = "do",
        [SyntaxKind.CatchClause] = "catch",
        [SyntaxKind.ConditionalExpression] = "?:",
        [SyntaxKind.CaseSwitchLabel] = "case",
        [SyntaxKind.CasePatternSwitchLabel] = "case",
        [SyntaxKind.LogicalAndExpression] = "&&",
        [SyntaxKind.LogicalOrExpression] = "||",
        [SyntaxKind.CoalesceExpression] = "??",
        [SyntaxKind.CoalesceAssignmentExpression] = "??=",
    };

    public static IReadOnlyList<DecisionEvidence> Collect(SyntaxNode unit) =>
        [.. FunctionUnits.OwnNodes(unit)
            .Select(node => (Kind: Classify(node), Node: node))
            .Where(item => item.Kind is not null)
            .Select(item => new DecisionEvidence(item.Kind!, SourceSpans.LineOf(item.Node)))];

    private static string? Classify(SyntaxNode node)
    {
        if (Decisions.TryGetValue(node.Kind(), out var kind))
        {
            return kind;
        }

        return node is SwitchExpressionArmSyntax arm && !IsUnconditionalDiscard(arm) ? "arm" : null;
    }

    private static bool IsUnconditionalDiscard(SwitchExpressionArmSyntax arm) =>
        arm.Pattern is DiscardPatternSyntax && arm.WhenClause is null;
}
