using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;

namespace CodeShield.CSharp.Metrics;

/// <summary>Counts physical lines that contain at least one non-trivia token.</summary>
internal static class CodeLineCounter
{
    public static int Count(SyntaxNode node) => Lines(node.DescendantTokens()).Count;

    private static HashSet<int> Lines(IEnumerable<SyntaxToken> tokens)
    {
        var lines = new HashSet<int>();
        foreach (var token in tokens.Where(token => !token.IsMissing && token.Span.Length > 0))
        {
            var span = token.GetLocation().GetLineSpan();
            lines.UnionWith(Enumerable.Range(span.StartLinePosition.Line, span.EndLinePosition.Line - span.StartLinePosition.Line + 1));
        }

        return lines;
    }
}
