using CodeShield.Abstractions;

namespace CodeShield.CSharp.Planning;

/// <summary>Builds the project-reference module graph from every project of the snapshot.</summary>
internal static class ModuleGraphBuilder
{
    public static ModuleGraphFacts Build(IReadOnlyList<ContextPlan> plans, RepoPaths paths)
    {
        var modules = plans.Select(plan => plan.CsprojPath).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToList();
        var references = plans
            .Where(plan => plan.Project is not null)
            .SelectMany(plan => plan.Project!.Items.ProjectReferences.Select(target => (From: plan.CsprojPath, Target: target)))
            .Distinct()
            .ToList();
        var resolved = references.Select(item => (item.From, To: paths.ToRepoPath(item.Target), item.Target)).ToList();
        return new ModuleGraphFacts(modules, Edges(resolved), [], [.. Unresolved(resolved), .. LoadFailures(plans)])
        {
            Contexts = [.. FrameworkSlices(plans, paths)],
        };
    }

    /// <summary>One slice per target framework: every project resolved for that framework, falling back to its newest context.</summary>
    private static IEnumerable<ContextGraph> FrameworkSlices(IReadOnlyList<ContextPlan> plans, RepoPaths paths)
    {
        var loaded = plans.Where(plan => plan.Project is not null).ToList();
        foreach (var framework in loaded.Select(plan => plan.Framework ?? string.Empty).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
        {
            var members = loaded.GroupBy(plan => plan.CsprojPath, StringComparer.Ordinal)
                .Select(group => group.FirstOrDefault(plan => (plan.Framework ?? string.Empty) == framework)
                    ?? group.OrderBy(plan => plan.Framework, StringComparer.Ordinal).Last());
            yield return new ContextGraph(framework, SliceEdges(members, paths), []);
        }
    }

    private static List<ModuleEdge> SliceEdges(IEnumerable<ContextPlan> members, RepoPaths paths) =>
        [.. members.SelectMany(plan => plan.Project!.Items.ProjectReferences.Select(paths.ToRepoPath).OfType<string>()
                .Select(to => new ModuleEdge(plan.CsprojPath, to, false)))
            .Distinct()
            .OrderBy(edge => edge.From, StringComparer.Ordinal).ThenBy(edge => edge.To, StringComparer.Ordinal)];
    private static List<ModuleEdge> Edges(List<(string From, string? To, string Target)> resolved) =>
        [.. resolved.Where(item => item.To is not null)
            .Select(item => new ModuleEdge(item.From, item.To!, false))
            .Distinct()
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)];

    private static IEnumerable<Reason> Unresolved(List<(string From, string? To, string Target)> resolved) =>
        resolved.Where(item => item.To is null)
            .Select(item => new Reason(ReasonCodes.MissingDependency, $"{item.From} references {Path.GetFileName(item.Target)} which is not part of the repository."));

    private static IEnumerable<Reason> LoadFailures(IReadOnlyList<ContextPlan> plans) =>
        plans.Where(plan => plan.Project is null)
            .Select(plan => plan.CsprojPath).Distinct(StringComparer.Ordinal)
            .Select(csproj => new Reason(ReasonCodes.ProjectLoadFailed, $"{csproj} could not be evaluated; its references are unknown."));
}
