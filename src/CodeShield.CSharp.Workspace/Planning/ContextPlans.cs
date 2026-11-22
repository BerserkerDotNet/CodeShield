using CodeShield.CSharp.Evaluation;

namespace CodeShield.CSharp.Planning;

/// <summary>One analysis context (project plus framework) before it is analyzed.</summary>
internal sealed record ContextPlan(
    string ContextId,
    string CsprojPath,
    string? Framework,
    EvaluatedProject? Project,
    string? Error,
    IReadOnlyList<string> Owned);

internal static class ContextPlans
{
    public static IReadOnlyList<ContextPlan> Build(IEnumerable<string> csprojPaths, AnalysisEnvironment environment) =>
        [.. csprojPaths.OrderBy(path => path, StringComparer.Ordinal).SelectMany(path => PlansOf(path, environment))];

    private static IEnumerable<ContextPlan> PlansOf(string csproj, AnalysisEnvironment environment)
    {
        var evaluation = environment.Evaluator.Evaluate(environment.Paths.ToFullPath(csproj));
        return evaluation.Contexts.Select(context => new ContextPlan(
            $"{csproj}|{context.Framework}",
            csproj,
            context.Framework,
            context.Project,
            context.Error,
            OwnedFiles(context.Project, environment.Paths)));
    }

    private static string[] OwnedFiles(EvaluatedProject? project, RepoPaths paths) =>
        project is null
            ? []
            : [.. project.Items.Compile.Select(paths.ToRepoPath).OfType<string>().Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)];
}
