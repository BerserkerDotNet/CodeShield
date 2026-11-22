using System.Collections.Concurrent;
using System.Globalization;
using CodeShield.Abstractions;
using CodeShield.CSharp.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CodeShield.CSharp.Correctness;

internal sealed record CorrectnessInput(
    Compilation Compilation,
    AnalyzerSet Analyzers,
    IReadOnlyDictionary<SyntaxTree, string> OwnedTrees,
    string ContextId);

internal sealed record CorrectnessOutcome(IReadOnlyList<DiagnosticFact> Diagnostics, IReadOnlyList<Reason> Failures);

/// <summary>Runs only the pinned correctness rules, with explicit options, over one compilation.</summary>
internal static class CorrectnessRunner
{
    public static async Task<CorrectnessOutcome> RunAsync(CorrectnessInput input, CancellationToken cancellationToken)
    {
        if (input.Analyzers.Error is { } error)
        {
            return Failed(ReasonCodes.MissingAnalyzer, error, RuleIds.Correctness, input.ContextId);
        }

        var suppressed = SuppressionDetector.Suppressed(input.OwnedTrees.Keys).Select(RuleIds.ForAnalyzer).ToList();
        var exceptions = new ConcurrentBag<string>();
        var diagnostics = await Analyze(input, exceptions, cancellationToken);
        if (!exceptions.IsEmpty)
        {
            return Failed(ReasonCodes.MissingAnalyzer, exceptions.First(), RuleIds.Correctness, input.ContextId);
        }

        var facts = diagnostics.Select(diagnostic => ToFact(diagnostic, input)).OfType<DiagnosticFact>()
            .Where(fact => !suppressed.Contains(RuleIds.ForAnalyzer(fact.AnalyzerId)));
        return new CorrectnessOutcome(Sort(facts), RuleIds.Fail(ReasonCodes.SuppressedRule, "A selected rule is suppressed in source.", suppressed, input.ContextId));
    }

    private static CorrectnessOutcome Failed(string code, string message, IEnumerable<string> rules, string contextId) =>
        new([], RuleIds.Fail(code, message, rules, contextId));

    private static async Task<IReadOnlyList<Diagnostic>> Analyze(CorrectnessInput input, ConcurrentBag<string> exceptions, CancellationToken cancellationToken)
    {
        var compilation = input.Compilation.WithOptions(input.Compilation.Options.WithSpecificDiagnosticOptions(SpecificOptions(input.Analyzers)));
        var options = new CompilationWithAnalyzersOptions(
            new AnalyzerOptions([], new ExplicitAnalyzerOptions()),
            (exception, analyzer, _) => exceptions.Add($"{analyzer.GetType().Name}: {exception.Message}"),
            concurrentAnalysis: true,
            logAnalyzerExecutionTime: false);
        var running = compilation.WithAnalyzers(input.Analyzers.Analyzers, options);
        return await running.GetAnalyzerDiagnosticsAsync(cancellationToken);
    }

    private static Dictionary<string, ReportDiagnostic> SpecificOptions(AnalyzerSet set) =>
        set.Analyzers.SelectMany(analyzer => analyzer.SupportedDiagnostics).Select(descriptor => descriptor.Id).Distinct()
            .ToDictionary(id => id, id => AnalyzerSetFactory.SelectedIds.Contains(id) ? ReportDiagnostic.Warn : ReportDiagnostic.Suppress);

    private static DiagnosticFact? ToFact(Diagnostic diagnostic, CorrectnessInput input)
    {
        var tree = diagnostic.Location.SourceTree;
        if (tree is null || !AnalyzerSetFactory.SelectedIds.Contains(diagnostic.Id) || !input.OwnedTrees.TryGetValue(tree, out var file))
        {
            return null;
        }

        var node = tree.GetRoot().FindNode(diagnostic.Location.SourceSpan);
        return new DiagnosticFact(diagnostic.Id, file, SourceSpans.Of(diagnostic.Location), diagnostic.GetMessage(CultureInfo.InvariantCulture), UnitIds.ForEnclosingMember(node, file));
    }

    private static List<DiagnosticFact> Sort(IEnumerable<DiagnosticFact> facts) =>
        [.. facts
            .OrderBy(fact => fact.File, StringComparer.Ordinal)
            .ThenBy(fact => fact.Span.StartLine)
            .ThenBy(fact => fact.Span.StartColumn)
            .ThenBy(fact => fact.AnalyzerId, StringComparer.Ordinal)];
}
