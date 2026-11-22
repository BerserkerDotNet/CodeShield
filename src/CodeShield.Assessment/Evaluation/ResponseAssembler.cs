using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rules;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Evaluation;

public static class ResponseAssembler
{
    public const string ContractVersion = "1";

    public static string Native(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    public static EvaluationResponse Failure(string status, int threshold, IReadOnlyList<Reason> reasons) => new(
        ContractVersion, status, null, null, null, null, null, new Dictionary<string, string>(),
        null, threshold, null, [], [], [], reasons, []);

    public static int ScoreOf(IEnumerable<RuleFinding> findings) => Math.Max(0, 100 - findings.Sum(finding => finding.Deduction));

    public static IReadOnlyList<RuleFinding> Sort(IEnumerable<RuleFinding> findings, Ruleset ruleset)
    {
        var order = ruleset.Rules.Select((rule, index) => (rule.Id, index)).ToDictionary(pair => pair.Id, pair => pair.index);
        return [.. findings.OrderBy(finding => order[finding.Rule.Id])
            .ThenBy(finding => finding.ContextId, StringComparer.Ordinal)
            .ThenBy(FirstFile, StringComparer.Ordinal)
            .ThenBy(FirstLine)];
    }

    private static string FirstFile(RuleFinding finding) => finding.Locations.Count > 0 ? finding.Locations[0].File : string.Empty;

    private static int FirstLine(RuleFinding finding) => finding.Locations.Count > 0 ? finding.Locations[0].StartLine : 0;

    public static IReadOnlyList<CategoryScore> Categories(Ruleset ruleset, IEnumerable<RuleFinding> findings)
    {
        var all = findings.ToList();
        return [.. ruleset.Rules.Select(rule => rule.Category).Distinct()
            .Select(category => new CategoryScore(category, all.Where(finding => finding.Rule.Category == category).Sum(finding => finding.Deduction)))];
    }

    public static FindingDto ToDto(RuleFinding finding) => new(
        finding.Rule.Id, finding.Rule.Definition, finding.Rule.Intent, Native(finding.ContextId), finding.Attribution,
        finding.Deduction, NativeLocations(finding.Locations), finding.Actual, finding.Limit,
        [.. finding.Evidence.Take(1)]);

    public static RuleDetailFindingDto ToDetail(RuleFinding finding) => new(
        Native(finding.ContextId), finding.Attribution, NativeLocations(finding.Locations), finding.Actual, finding.Limit,
        finding.Deduction, finding.Calculation, finding.Evidence);

    public static CoverageEntry Native(CoverageEntry entry) =>
        entry with { ContextId = Native(entry.ContextId), Reasons = [.. entry.Reasons.Select(NativeReason)] };

    public static Reason NativeReason(Reason reason) =>
        reason with { ContextId = reason.ContextId is null ? null : Native(reason.ContextId) };

    private static IReadOnlyList<FindingLocation> NativeLocations(IReadOnlyList<FindingLocation> locations) =>
        [.. locations.Select(location => location with { File = Native(location.File) })];
}
