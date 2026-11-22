using CodeShield.Abstractions;
using CodeShield.Core.Rules;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Evaluation;

/// <summary>Materializes the baseline and runs all adapters on both snapshots.</summary>
public sealed class SnapshotCollector(JobPlanner planner, SupportedFiles supported, ISnapshotMaterializer materializer)
{
    private sealed record Pass(SnapshotAnalysis Current, SnapshotAnalysis Baseline, string BaselineRoot)
    {
        /// <summary>Repository inputs of the current tree plus every external input either snapshot read, except the transient baseline export itself.</summary>
        public IEnumerable<InputRecord> Discovered => Current.Inputs
            .Concat(Baseline.Inputs.Where(input => input.Scope == InputScopes.External && !IsUnder(input.Path, BaselineRoot)));

        private static bool IsUnder(string path, string root) =>
            path.Replace('\\', '/').StartsWith(Path.GetFullPath(root).Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<AnalysisBundle> CollectAsync(ChangeScope scope, CancellationToken cancellationToken)
    {
        var changedInputs = HashChangedFiles(scope);
        using var baselineSnapshot = await materializer
            .MaterializeAsync(scope.Root, scope.BaselineCommit, scope.BaselinePaths, cancellationToken).ConfigureAwait(false);
        var first = await PassAsync(scope, baselineSnapshot.Path, cancellationToken).ConfigureAwait(false);
        var tokens = await TokenizeAsync(scope, cancellationToken).ConfigureAwait(false);
        var second = await PassAsync(scope, baselineSnapshot.Path, cancellationToken).ConfigureAwait(false);
        var (architecture, architectureInput) = ArchitectureConfigLoader.Load(scope.Root);
        IEnumerable<InputRecord> inputs = [.. changedInputs.Inputs, .. first.Discovered, .. tokens.Inputs];
        return new AnalysisBundle(first.Current, first.Baseline, tokens.Files, tokens.Failures, architecture,
            [.. inputs.Concat(architectureInput is null ? [] : [architectureInput])],
            SnapshotInventory.Differs(first.Discovered, second.Discovered), changedInputs.Unreadable);
    }

    private async Task<Pass> PassAsync(ChangeScope scope, string baselineRoot, CancellationToken cancellationToken)
    {
        var currentTask = AnalysisRunner.RunAsync(planner.PlanCurrent(scope), cancellationToken);
        var baselineTask = AnalysisRunner.RunAsync(planner.PlanBaseline(scope, baselineRoot), cancellationToken);
        return new Pass(await currentTask.ConfigureAwait(false), await baselineTask.ConfigureAwait(false), baselineRoot);
    }

    private static InputHasher.Result HashChangedFiles(ChangeScope scope) =>
        InputHasher.HashAll(scope.Root, scope.Map.Changes.Where(change => change.Kind != Git.ChangeKind.Deleted).Select(change => change.Path));

    private async Task<(IReadOnlyDictionary<string, IReadOnlyList<FileTokens>> Files, IReadOnlyList<Reason> Failures, List<InputRecord> Inputs)> TokenizeAsync(
        ChangeScope scope, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, IReadOnlyList<FileTokens>>();
        var inputs = new List<InputRecord>();
        var failures = new List<Reason>();
        foreach (var analyzer in supported.Analyzers.Where(analyzer => planner.PlanCurrent(scope).Any(job => job.Analyzer == analyzer)))
        {
            var sources = scope.CurrentPaths.Where(analyzer.IsSource).ToList();
            var hashed = InputHasher.HashAll(scope.Root, sources);
            inputs.AddRange(hashed.Inputs);
            failures.AddRange(hashed.Unreadable);
            var result = await TokenizeOneAsync(analyzer.Language, analyzer, new TokenizeRequest(scope.Root, sources), cancellationToken).ConfigureAwait(false);
            files[analyzer.Language] = result.Files;
            failures.AddRange(result.Failures);
        }

        return (files, failures, inputs);
    }

    private static async Task<TokenizeResult> TokenizeOneAsync(
        string language, ITokenizer tokenizer, TokenizeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await tokenizer.TokenizeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new TokenizeResult([], [new Reason(ReasonCodes.ProjectLoadFailed, $"{language} tokenization failed: {exception.Message}")]);
        }
    }
}

