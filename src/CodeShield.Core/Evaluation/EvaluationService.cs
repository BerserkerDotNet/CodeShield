using CodeShield.Abstractions;
using CodeShield.Core.Configuration;
using CodeShield.Core.Contracts;
using CodeShield.Core.Git;
using CodeShield.Core.Rules;
using CodeShield.Core.Rulesets;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Evaluation;

/// <summary>Implements <c>evaluate_quality</c>: scope, analysis, rules, scoring, snapshot staleness and storage.</summary>
public sealed class EvaluationService(
    ScopeDiscovery discovery,
    JobPlanner planner,
    SnapshotCollector collector,
    RuleRunner runner,
    EvaluationStore store)
{
    public async Task<EvaluationResponse> EvaluateAsync(EvaluateRequest request, CancellationToken cancellationToken)
    {
        var validation = RequestValidator.Validate(request);
        if (validation.Error is not null)
        {
            return validation.Error;
        }

        try
        {
            return await RunAsync(validation.Request!, cancellationToken).ConfigureAwait(false);
        }
        catch (GitException exception)
        {
            return ResponseAssembler.Failure(EvaluationStatus.Error, DefaultRuleset.Threshold, [exception.ToReason()]);
        }
        catch (ConfigurationException exception)
        {
            return ResponseAssembler.Failure(EvaluationStatus.Error, DefaultRuleset.Threshold, [new Reason(ReasonCodes.InvalidConfiguration, exception.Message)]);
        }
        catch (ScopeConflictException exception)
        {
            return ResponseAssembler.Failure(EvaluationStatus.Incomplete, DefaultRuleset.Threshold, [new Reason(ReasonCodes.UnreconciledScope, exception.Message)]);
        }
    }

    private async Task<EvaluationResponse> RunAsync(ValidatedRequest request, CancellationToken cancellationToken)
    {
        var scope = await discovery.DiscoverAsync(request.Workspace, request.Baseline, cancellationToken).ConfigureAwait(false);
        var configuration = ConfigLoader.Load(scope.Root, scope.CurrentPaths, request.Ruleset);
        if (!planner.HasWork(scope))
        {
            return NoChanges(request.Ruleset, scope, configuration);
        }

        var collected = await collector.CollectAsync(scope, cancellationToken).ConfigureAwait(false);
        var bundle = collected with { Policy = configuration.Policy, Inputs = [.. collected.Inputs, .. configuration.Inputs] };
        var run = runner.Run(request.Ruleset, scope, bundle);
        var current = await discovery.DiscoverAsync(request.Workspace, scope.BaselineCommit, cancellationToken).ConfigureAwait(false);
        var stale = bundle.InputsDrifted || current.Fingerprint != scope.Fingerprint || SnapshotIdentity.IsStale(scope.Root, bundle.Inputs);
        return Complete(request.Ruleset, scope, (bundle, configuration), run, stale);
    }

    private static EvaluationResponse NoChanges(Ruleset ruleset, ChangeScope scope, LoadedConfiguration configuration)
    {
        var disclosure = ConfigDisclosureBuilder.Build(configuration, scope.Map, null);
        var message = disclosure is { Changed: true }
            ? "Only CodeShield configuration changed; no C#, TypeScript or Kotlin source or project configuration changed, so nothing was analyzed."
            : "Git found no changed C#, TypeScript or Kotlin files or relevant project configuration; nothing was analyzed.";
        return Base(ruleset, scope, EvaluationStatus.NoChanges, [new Reason("NO_SUPPORTED_CHANGES", message)]) with { Configuration = disclosure };
    }

    private EvaluationResponse Complete(
        Ruleset ruleset, ChangeScope scope, (AnalysisBundle Bundle, LoadedConfiguration Configuration) analysis, RuleRun run, bool stale)
    {
        var (bundle, configuration) = analysis;
        var coverage = run.Coverage.Select(ResponseAssembler.Native).ToList();
        var failed = coverage.Where(entry => entry.Status == CoverageStatus.Failed).ToList();
        var findings = ResponseAssembler.Sort(run.Findings, ruleset);
        var complete = failed.Count == 0 && !stale;
        var staleReason = stale ? [new Reason(ReasonCodes.StaleSnapshot, "An analysis input was added, removed or changed while the evaluation was running; re-run the evaluation.")] : Array.Empty<Reason>();
        var score = ResponseAssembler.ScoreOf(findings);
        return Base(ruleset, scope, complete ? EvaluationStatus.Complete : EvaluationStatus.Incomplete,
            [.. failed.SelectMany(entry => entry.Reasons).Concat(staleReason).Distinct()]) with
        {
            EvaluationId = store.Add(ruleset, findings),
            SnapshotId = SnapshotIdentity.Compute(bundle.Inputs),
            AnalyzerVersions = Versions(bundle),
            Score = complete ? score : null,
            Passed = complete ? score >= ruleset.Threshold : null,
            Categories = ResponseAssembler.Categories(ruleset, findings),
            Findings = [.. findings.Select(ResponseAssembler.ToDto)],
            Coverage = coverage,
            Configuration = ConfigDisclosureBuilder.Build(configuration, scope.Map, run.ExcludedFindings),
        };
    }

    private static Dictionary<string, string> Versions(AnalysisBundle bundle) =>
        bundle.Current.Versions.Concat(bundle.Baseline.Versions)
            .GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.First().Value);

    private static EvaluationResponse Base(Ruleset ruleset, ChangeScope scope, string status, IReadOnlyList<Reason> reasons) => new(
        ResponseAssembler.ContractVersion, status, null, scope.BaselineCommit, null, ruleset.Name, ruleset.Version,
        new Dictionary<string, string>(), null, ruleset.Threshold, null, [], [], [],
        [.. reasons.Select(ResponseAssembler.NativeReason)], ScopeEntries(scope));

    private static List<ScopeEntry> ScopeEntries(ChangeScope scope) =>
        [.. scope.Map.Changes.Select(change => new ScopeEntry(ResponseAssembler.Native(change.Path), change.Kind.ToString().ToLowerInvariant()))];
}
