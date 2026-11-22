using CodeShield.Abstractions;
using Microsoft.CodeAnalysis;

namespace CodeShield.CSharp.Metrics;

/// <summary>Builds method and file facts for a parsed source file.</summary>
internal static class FileFactBuilder
{
    public static FileFact Build(string repoPath, SyntaxTree tree, bool generated)
    {
        var root = tree.GetRoot();
        var methods = generated ? [] : Methods(repoPath, root);
        return new FileFact(repoPath, CodeLineCounter.Count(root), generated, methods);
    }

    private static List<MethodFact> Methods(string repoPath, SyntaxNode root) =>
        [.. FunctionUnits.Find(root).Select(unit => Method(repoPath, unit))];

    private static MethodFact Method(string repoPath, SyntaxNode unit)
    {
        var decisions = ComplexityCounter.Collect(unit);
        var (nesting, points) = NestingCounter.Measure(unit);
        return new MethodFact(
            UnitIds.For(unit, repoPath),
            UnitIds.LocalName(unit),
            SourceSpans.Of(unit),
            decisions.Count + 1,
            nesting,
            CodeLineCounter.Count(unit),
            FunctionUnits.ParameterCount(unit),
            decisions,
            points);
    }
}
