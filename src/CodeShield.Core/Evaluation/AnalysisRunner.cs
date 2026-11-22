using CodeShield.Abstractions;
using CodeShield.Core.Rules;

namespace CodeShield.Core.Evaluation;

/// <summary>What one adapter must analyze in one snapshot.</summary>
public sealed record AnalysisJob(
    ILanguageAnalyzer Analyzer,
    string Root,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Focus,
    IReadOnlyList<string> ChangedConfig,
    bool IsBaseline);

public static class AnalysisRunner
{
    public static async Task<SnapshotAnalysis> RunAsync(IReadOnlyList<AnalysisJob> jobs, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(jobs.Select(job => RunJobAsync(job, cancellationToken))).ConfigureAwait(false);
        var graph = new ModuleGraphFacts(
            [.. results.SelectMany(result => result.Graph.Modules).Distinct()],
            [.. results.SelectMany(result => result.Graph.Edges).Distinct()],
            [.. results.SelectMany(result => result.Graph.FileImports).Distinct()],
            [.. results.SelectMany(result => result.Graph.Unresolved)])
        {
            Contexts = [.. results.SelectMany(result => result.Graph.Contexts)],
        };
        var facts = new SnapshotFacts([.. results.SelectMany(result => result.Contexts)], graph);
        return new SnapshotAnalysis(facts, results, [.. results.SelectMany(result => result.Inputs)]);
    }

    private static async Task<LanguageAnalysis> RunJobAsync(AnalysisJob job, CancellationToken cancellationToken)
    {
        var request = new AnalysisRequest(job.Root, job.Files, job.Focus, job.ChangedConfig, job.IsBaseline);
        try
        {
            return await job.Analyzer.AnalyzeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var reason = new Reason(ReasonCodes.ProjectLoadFailed, $"{job.Analyzer.Language} analysis failed: {exception.Message}");
            return new LanguageAnalysis(job.Analyzer.Language, new Dictionary<string, string>(), [], ModuleGraphFacts.Empty, [], [], [reason]);
        }
    }
}
