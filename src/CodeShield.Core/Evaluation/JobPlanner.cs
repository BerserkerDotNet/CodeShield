using CodeShield.Abstractions;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Evaluation;

/// <summary>Decides which adapters run on which files for the current and baseline snapshots.</summary>
public sealed class JobPlanner(SupportedFiles supported)
{
    public IReadOnlyList<AnalysisJob> PlanCurrent(ChangeScope scope) =>
        [.. ActiveAnalyzers(scope).Select(analyzer => Plan(analyzer, scope, scope.Root, scope.CurrentPaths, isBaseline: false))];

    public IReadOnlyList<AnalysisJob> PlanBaseline(ChangeScope scope, string root) =>
        [.. ActiveAnalyzers(scope).Select(analyzer => Plan(analyzer, scope, root, scope.BaselinePaths, isBaseline: true))];

    public bool HasWork(ChangeScope scope) => ActiveAnalyzers(scope).Any();

    /// <summary>
    /// Adapters whose files changed, plus, when <c>.quality-architecture.json</c> changed, every adapter with files in either snapshot.
    /// An adapter with no files has no modules for the architecture rules to check, so it is not run and its runtime is not required.
    /// </summary>
    private IEnumerable<ILanguageAnalyzer> ActiveAnalyzers(ChangeScope scope)
    {
        var architectureChanged = scope.Map.Changes.Any(change => change.Path == SupportedFiles.ArchitectureConfig);
        return supported.Analyzers.Where(analyzer =>
            scope.Map.Changes.Any(change => Touches(analyzer, change)) || (architectureChanged && HasFiles(analyzer, scope)));
    }

    private static bool HasFiles(ISourceClassifier analyzer, ChangeScope scope) =>
        scope.CurrentPaths.Concat(scope.BaselinePaths).Any(path => Relevant(analyzer, path));

    private static bool Touches(ISourceClassifier analyzer, FileChange change) =>
        Relevant(analyzer, change.Path) || (change.OldPath is not null && Relevant(analyzer, change.OldPath));

    private static bool Relevant(ISourceClassifier analyzer, string path) =>
        analyzer.IsSource(path) || analyzer.IsConfiguration(path);

    private static AnalysisJob Plan(ILanguageAnalyzer analyzer, ChangeScope scope, string root, IReadOnlyList<string> paths, bool isBaseline)
    {
        var files = paths.Where(path => Relevant(analyzer, path)).ToList();
        var fileSet = files.ToHashSet(StringComparer.Ordinal);
        var focus = scope.Map.Changes.Select(change => PathIn(change, isBaseline))
            .OfType<string>().Where(path => analyzer.IsSource(path) && fileSet.Contains(path)).ToList();
        var config = scope.Map.Changes.Select(change => PathIn(change, isBaseline))
            .OfType<string>().Where(path => analyzer.IsConfiguration(path) && fileSet.Contains(path)).ToList();
        return new AnalysisJob(analyzer, root, files, focus, config, isBaseline);
    }

    private static string? PathIn(FileChange change, bool isBaseline) => isBaseline
        ? change.Kind == Git.ChangeKind.Added ? null : change.OldPath ?? change.Path
        : change.Kind == Git.ChangeKind.Deleted ? null : change.Path;
}

