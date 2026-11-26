using CodeShield.Abstractions;
using CodeShield.Core.Configuration;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rules;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Evaluation;

/// <summary>Marks a coverage entry <c>excluded</c> when its rule is excluded at every anchor path of that context.</summary>
public sealed class CoverageExclusions(RulePolicy policy, SnapshotFacts facts)
{
    private const string Analysis = "analysis";

    public CoverageEntry Apply(CoverageEntry entry) =>
        IsExcluded(entry) ? entry with { Status = CoverageStatus.Excluded, Reasons = [] } : entry;

    private bool IsExcluded(CoverageEntry entry)
    {
        if (entry.Reasons.Any(reason => reason.Code == ReasonCodes.UnreadableSource))
        {
            return false;
        }

        return IsGlobal(entry.ContextId)
            ? ReasonsExcluded(entry) || policy.ExcludedEverywhere(entry.RuleId)
            : Anchors(entry.RuleId, entry.ContextId).All(anchor => Excludes(entry.RuleId, anchor));
    }

    /// <summary>A failure that names only paths inside excluded code does not make the result incomplete.</summary>
    private bool ReasonsExcluded(CoverageEntry entry) =>
        entry.Status == CoverageStatus.Failed
        && entry.Reasons.Count > 0
        && entry.Reasons.All(reason => SubjectOf(reason) is { } id && Excludes(entry.RuleId, PathOf(id)));

    /// <summary>The project or file a failure is about; graph failures were re-keyed to the repository, so the original is looked up.</summary>
    private string? SubjectOf(Reason reason)
    {
        if (reason.ContextId is { } id && !IsGlobal(id))
        {
            return id;
        }

        return facts.Graph.Unresolved.FirstOrDefault(original => original.Code == reason.Code && original.Message == reason.Message)?.ContextId;
    }

    private List<string> Anchors(string ruleId, string contextId)
    {
        var context = facts.Contexts.FirstOrDefault(candidate => candidate.ContextId == contextId);
        var manifest = context?.ModulePath ?? PathOf(contextId);
        return context is null || IsModuleLevel(ruleId) ? [manifest] : [manifest, .. context.OwnedFiles];
    }

    private bool Excludes(string ruleId, string path) =>
        ruleId == Analysis ? policy.IsFullyExcluded(path) : policy.IsExcluded(ruleId, path);

    private static bool IsModuleLevel(string ruleId) =>
        ruleId is RuleIds.MainSequence or RuleIds.ModuleCycle or RuleIds.Boundary;

    private static bool IsGlobal(string contextId) =>
        contextId == "repository" || Languages.All.Contains(contextId);

    private static string PathOf(string contextId)
    {
        var separator = contextId.IndexOf('|', StringComparison.Ordinal);
        return separator < 0 ? contextId : contextId[..separator];
    }
}
