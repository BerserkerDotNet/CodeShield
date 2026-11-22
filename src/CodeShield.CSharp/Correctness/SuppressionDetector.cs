using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeShield.CSharp.Correctness;

/// <summary>Finds suppressions that would stop a selected analyzer from reporting (pragmas, SuppressMessage, global suppressions).</summary>
internal static class SuppressionDetector
{
    /// <summary>The selected analyzer IDs suppressed anywhere in <paramref name="trees"/>; a blanket pragma suppresses all of them.</summary>
    public static IReadOnlySet<string> Suppressed(IEnumerable<SyntaxTree> trees)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in trees.Select(tree => tree.GetRoot()))
        {
            found.UnionWith(PragmaIds(root));
            found.UnionWith(SuppressMessageIds(root));
        }

        return found.Contains("*") ? AnalyzerSetFactory.SelectedIds : found.Where(AnalyzerSetFactory.SelectedIds.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> PragmaIds(SyntaxNode root) =>
        root.DescendantTrivia(descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure())
            .OfType<PragmaWarningDirectiveTriviaSyntax>()
            .Where(pragma => pragma.DisableOrRestoreKeyword.IsKind(SyntaxKind.DisableKeyword))
            .SelectMany(pragma => pragma.ErrorCodes.Count == 0 ? ["*"] : pragma.ErrorCodes.Select(code => code.ToString().Trim()));

    private static IEnumerable<string> SuppressMessageIds(SyntaxNode root) =>
        root.DescendantNodes().OfType<AttributeSyntax>()
            .Where(IsSuppressMessage)
            .Select(attribute => CheckId(attribute.ArgumentList?.Arguments.Skip(1).FirstOrDefault()))
            .OfType<string>();

    private static bool IsSuppressMessage(AttributeSyntax attribute)
    {
        var name = attribute.Name.ToString().Split('.')[^1];
        return name is "SuppressMessage" or "SuppressMessageAttribute";
    }

    private static string? CheckId(AttributeArgumentSyntax? argument) =>
        argument?.Expression is LiteralExpressionSyntax literal ? literal.Token.ValueText.Split(':')[0].Trim() : null;
}
