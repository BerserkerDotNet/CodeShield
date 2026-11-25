using CodeShield.Abstractions;
using CodeShield.Core.Configuration;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rules;
using CodeShield.Core.Rulesets;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Evaluation;

public sealed record RuleRun(
    IReadOnlyList<RuleFinding> Findings,
    IReadOnlyList<CoverageEntry> Coverage,
    IReadOnlyDictionary<string, int>? ExcludedFindings = null);

/// <summary>Runs every rule over the bundle and adds coverage for language-wide failures.</summary>
public sealed class RuleRunner(IEnumerable<IRule> rules)
{
    private readonly IReadOnlyList<IRule> _rules = [.. rules];

    public RuleRun Run(Ruleset ruleset, ChangeScope scope, AnalysisBundle bundle)
    {
        var policy = bundle.Policy ?? RulePolicy.Defaults;
        var input = new RuleInput(ruleset, scope, bundle.Current.Facts, bundle.Baseline.Facts, bundle.Tokens,
            bundle.Architecture, GraphFailures(scope, bundle)) { Policy = policy };
        var findings = new List<RuleFinding>();
        var coverage = new List<CoverageEntry>();
        foreach (var output in _rules.Select(rule => rule.Evaluate(input)))
        {
            findings.AddRange(output.Findings);
            coverage.AddRange(output.Coverage);
        }

        coverage.AddRange(AnalysisFailures(bundle));
        return ExclusionFilter.Apply(new RuleRun(findings, coverage), bundle.Current.Facts, policy);
    }

    private static List<Reason> GraphFailures(ChangeScope scope, AnalysisBundle bundle)
    {
        var failures = bundle.Current.Facts.Graph.Unresolved
            .Where(reason => reason.ContextId is null || reason.Code == ReasonCodes.ProjectLoadFailed || scope.Map.IsChanged(reason.ContextId))
            .Select(reason => reason with { RuleId = null }).ToList();
        failures.AddRange(bundle.TokenFailures.Select(reason => reason with { RuleId = RuleIds.Duplication }));
        if (bundle.Architecture?.Error is { } error)
        {
            failures.Add(new Reason(ReasonCodes.ProjectLoadFailed, error, RuleIds.Boundary));
        }

        return failures;
    }

    private static IEnumerable<CoverageEntry> AnalysisFailures(AnalysisBundle bundle) =>
        (bundle.UnreadableInputs ?? []).Select(reason => new CoverageEntry("analysis", reason.ContextId ?? "repository", CoverageStatus.Failed, [reason]))
            .Concat(bundle.Current.Languages.SelectMany(language => language.Failures
                .Select(reason => new CoverageEntry(reason.RuleId ?? "analysis", language.Language, CoverageStatus.Failed, [reason]))))
            .Concat(bundle.Current.Languages.SelectMany(language => language.UnownedFiles
                .Select(file => new CoverageEntry("analysis", file, CoverageStatus.Failed,
                    [new Reason(ReasonCodes.UnresolvedOwnership, $"'{file}' is not owned by any analyzable project.", null, file)]))));
}
