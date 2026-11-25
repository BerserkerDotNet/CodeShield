using CodeShield.Abstractions;
using CodeShield.Core.Configuration;
using CodeShield.Core.Rules;

namespace CodeShield.Core.Evaluation;

/// <summary>Applies configured exclusions after the rules ran: excluded code is analyzed but never charged.</summary>
public static class ExclusionFilter
{
    public static RuleRun Apply(RuleRun run, SnapshotFacts facts, RulePolicy policy)
    {
        if (policy.Configs.Count == 0)
        {
            return run;
        }

        var dropped = run.Findings.Where(finding => IsExcluded(finding, policy)).ToList();
        var coverage = new CoverageExclusions(policy, facts);
        return new RuleRun(
            [.. run.Findings.Except(dropped)],
            [.. run.Coverage.Select(coverage.Apply)],
            dropped.GroupBy(finding => finding.Rule.Id).ToDictionary(group => group.Key, group => group.Count()));
    }

    /// <summary>A finding is dropped only when its rule is excluded at every one of its location files.</summary>
    private static bool IsExcluded(RuleFinding finding, RulePolicy policy) =>
        finding.Locations.Count > 0 && finding.Locations.All(location => policy.IsExcluded(finding.Rule.Id, location.File));
}
