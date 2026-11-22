using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;

namespace CodeShield.CSharp.Metrics;

/// <summary>Conversions from Roslyn locations to the one-based inclusive spans of the facts model.</summary>
internal static class SourceSpans
{
    public static SourceSpan Of(SyntaxNode node) => Of(node.GetLocation());

    public static SourceSpan Of(Location location)
    {
        var span = location.GetLineSpan().Span;
        return new SourceSpan(span.Start.Line + 1, span.Start.Character + 1, span.End.Line + 1, Math.Max(1, span.End.Character));
    }

    public static int LineOf(SyntaxNode node) => node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
