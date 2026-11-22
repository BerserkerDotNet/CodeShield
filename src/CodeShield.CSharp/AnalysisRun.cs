using CodeShield.Abstractions;
using CodeShield.CSharp.Building;
using CodeShield.CSharp.Correctness;
using CodeShield.CSharp.Evaluation;
using CodeShield.CSharp.Planning;

namespace CodeShield.CSharp;

/// <summary>One analysis of one snapshot. MSBuild must already be registered.</summary>
internal sealed class AnalysisRun(AnalysisRequest request, string assetsRoot)
{
    public async Task<LanguageAnalysis> ExecuteAsync(CancellationToken cancellationToken)
    {
        using var evaluator = new ProjectEvaluator();
        var ledger = new InputLedger();
        var environment = new AnalysisEnvironment(new RepoPaths(request.SnapshotRoot, request.Files), ledger, assetsRoot, evaluator);
        var plans = ContextPlans.Build(request.Files.Where(FileKinds.IsProject), environment);
        var selector = new ContextSelector(
            environment.Paths,
            request.FocusFiles.Where(FileKinds.IsSource).ToHashSet(StringComparer.Ordinal),
            request.ChangedConfigFiles.ToHashSet(StringComparer.Ordinal));
        var contexts = await AnalyzeSelectedAsync(plans.Where(selector.IsSelected).ToList(), environment, cancellationToken);
        return new LanguageAnalysis(
            Languages.CSharp,
            AnalyzerVersions.Current(),
            contexts,
            ModuleGraphBuilder.Build(plans, environment.Paths),
            ledger.Snapshot(),
            selector.Unowned(plans),
            []);
    }

    private async Task<List<ContextFacts>> AnalyzeSelectedAsync(List<ContextPlan> selected, AnalysisEnvironment environment, CancellationToken cancellationToken)
    {
        var analyzer = new ContextAnalyzer(new CompilationFactory(environment), new Lazy<AnalyzerSet>(() => AnalyzerSetFactory.Load(environment)));
        var focus = request.FocusFiles.ToHashSet(StringComparer.Ordinal);
        var results = new List<ContextFacts>();
        foreach (var plan in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ProjectInputs.Record(plan, environment);
            results.Add(await analyzer.AnalyzeAsync(new ContextJob(plan, focus), cancellationToken));
        }

        return results;
    }
}

