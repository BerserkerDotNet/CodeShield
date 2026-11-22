using CodeShield.Abstractions;
using CodeShield.CSharp.Building;
using CodeShield.CSharp.Cohesion;
using CodeShield.CSharp.Correctness;
using CodeShield.CSharp.Metrics;
using CodeShield.CSharp.Planning;
using Microsoft.CodeAnalysis;

namespace CodeShield.CSharp;

/// <summary>Everything needed to analyze one selected context.</summary>
internal sealed record ContextJob(ContextPlan Plan, IReadOnlySet<string> Focus);

/// <summary>Analyzes one context: compiles it and derives method, class, type and diagnostic facts.</summary>
internal sealed class ContextAnalyzer(CompilationFactory factory, Lazy<AnalyzerSet> analyzers)
{
    public async Task<ContextFacts> AnalyzeAsync(ContextJob job, CancellationToken cancellationToken)
    {
        if (job.Plan.Project is not { } project)
        {
            return Failed(job.Plan, job.Plan.Error ?? "Project evaluation failed.");
        }

        try
        {
            return await AnalyzeProjectAsync(job, project, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failed(job.Plan, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task<ContextFacts> AnalyzeProjectAsync(ContextJob job, Evaluation.EvaluatedProject project, CancellationToken cancellationToken)
    {
        var built = factory.Create(project);
        var owned = built.Sources.Where(source => source.RepoPath is not null).ToList();
        var focusSources = owned.Where(source => job.Focus.Contains(source.RepoPath!)).ToList();
        var typeAware = await TypeAwareFactsAsync(job.Plan, built, owned, focusSources, cancellationToken);
        var failures = typeAware.Failures.Concat(Unreadable(job.Plan, built)).ToList();
        return new ContextFacts(
            job.Plan.ContextId, Languages.CSharp, job.Plan.CsprojPath, job.Plan.Framework, job.Plan.CsprojPath,
            job.Plan.Owned, [.. focusSources.Select(source => FileFactBuilder.Build(source.RepoPath!, source.Tree, source.Generated))],
            typeAware.Classes, typeAware.Types, typeAware.Diagnostics, failures);
    }

    private async Task<TypeAwareFacts> TypeAwareFactsAsync(
        ContextPlan plan, ProjectCompilation built, List<SourceFile> owned, List<SourceFile> focus, CancellationToken cancellationToken)
    {
        var scored = owned.Where(source => !source.Generated).ToList();
        var repoPaths = scored.ToDictionary(source => source.Tree, source => source.RepoPath!);
        var models = new SemanticModels(built.Compilation);
        var types = TypeFacts.Types(repoPaths.Keys, repoPaths, models);
        var missing = Missing(built);
        if (missing is not null)
        {
            return new TypeAwareFacts([], types, [], RuleIds.Fail(ReasonCodes.MissingDependency, missing, RuleIds.TypeAware, plan.ContextId));
        }

        var focusTrees = focus.Where(source => !source.Generated).Select(source => source.Tree);
        var correctness = await CorrectnessRunner.RunAsync(new CorrectnessInput(built.Compilation, analyzers.Value, repoPaths, plan.ContextId), cancellationToken);
        return new TypeAwareFacts(TypeFacts.Classes(focusTrees, repoPaths, models), types, correctness.Diagnostics, correctness.Failures);
    }

    private static string? Missing(ProjectCompilation built)
    {
        if (built.Missing.Count > 0)
        {
            return built.Missing[0];
        }

        var firstError = MissingTypeErrors.First(built.Compilation);
        return firstError is null ? null : $"The compilation has unresolved external types: {firstError}";
    }

    private static IEnumerable<Reason> Unreadable(ContextPlan plan, ProjectCompilation built) =>
        built.Unreadable.Select(path => new Reason(ReasonCodes.UnreadableSource, $"Could not read {Path.GetFileName(path)}.", null, plan.ContextId));

    private static ContextFacts Failed(ContextPlan plan, string message) =>
        new(plan.ContextId, Languages.CSharp, plan.CsprojPath, plan.Framework, plan.CsprojPath, plan.Owned, [], [], [], [],
            [new Reason(ReasonCodes.ProjectLoadFailed, message, null, plan.ContextId)]);

    private sealed record TypeAwareFacts(
        IReadOnlyList<ClassFact> Classes,
        IReadOnlyList<TypeFact> Types,
        IReadOnlyList<DiagnosticFact> Diagnostics,
        IReadOnlyList<Reason> Failures);
}
