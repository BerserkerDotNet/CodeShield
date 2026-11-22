namespace CodeShield.CSharp.Planning;

/// <summary>Chooses the contexts to analyze: those that own a focus file or are affected by a changed configuration file.</summary>
internal sealed class ContextSelector(RepoPaths paths, IReadOnlySet<string> focus, IReadOnlySet<string> changedConfig)
{
    public bool IsSelected(ContextPlan plan) =>
        plan.Owned.Any(focus.Contains)
        || changedConfig.Contains(plan.CsprojPath)
        || ImportsChanged(plan)
        || (plan.Project is null && HasFocusBelow(plan.CsprojPath));

    /// <summary>Focus files that no loadable project owns and that no failed project could own.</summary>
    public IReadOnlyList<string> Unowned(IReadOnlyList<ContextPlan> plans)
    {
        var owned = plans.SelectMany(plan => plan.Owned).ToHashSet(StringComparer.Ordinal);
        var failedRoots = plans.Where(plan => plan.Project is null).Select(plan => DirectoryOf(plan.CsprojPath)).ToList();
        return [.. focus.Where(file => !owned.Contains(file) && !failedRoots.Any(root => IsBelow(file, root))).OrderBy(file => file, StringComparer.Ordinal)];
    }

    private bool ImportsChanged(ContextPlan plan) =>
        plan.Project is not null
        && plan.Project.Items.Imports.Select(paths.ToRepoPath).OfType<string>().Any(changedConfig.Contains);

    private bool HasFocusBelow(string csproj) => focus.Any(file => IsBelow(file, DirectoryOf(csproj)));

    private static string DirectoryOf(string repoPath) =>
        repoPath.Contains('/') ? repoPath[..repoPath.LastIndexOf('/')] : "";

    private static bool IsBelow(string file, string directory) =>
        directory.Length == 0 || file.StartsWith(directory + "/", StringComparison.Ordinal);
}
