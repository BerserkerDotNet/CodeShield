using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Rules;

/// <summary>What a rule produced for one analysis context.</summary>
public sealed record ContextOutcome(IReadOnlyList<RuleFinding> Findings, IReadOnlySet<string> Assessed)
{
    public static ContextOutcome None { get; } = new([], new HashSet<string>());
}

/// <summary>Shared behaviour of rules evaluated once per analysis context, including coverage.</summary>
public abstract class ContextRuleBase : IRule
{
    public abstract IReadOnlyList<string> Ids { get; }

    public RuleOutput Evaluate(RuleInput input)
    {
        var output = new RuleOutput();
        foreach (var context in input.Current.Contexts)
        {
            var outcome = EvaluateContext(input, context);
            output.Findings.AddRange(outcome.Findings);
            output.Coverage.AddRange(IdsFor(context).Select(id => CoverageFor(input, context, id, outcome.Assessed.Contains(id))));
        }

        return output;
    }

    protected virtual IReadOnlyList<string> IdsFor(ContextFacts context) => Ids;

    protected abstract ContextOutcome EvaluateContext(RuleInput input, ContextFacts context);

    /// <summary>Finds the baseline counterpart of a current context, or null for a new context.</summary>
    protected static ContextFacts? BaselineOf(RuleInput input, ContextFacts context)
    {
        var baselineId = ContextIds.ToBaseline(context.ContextId, input.Map);
        return baselineId is null ? null : input.Baseline.FindContext(baselineId);
    }

    private static CoverageEntry CoverageFor(RuleInput input, ContextFacts context, string ruleId, bool assessed)
    {
        var reasons = FailuresFor(context, ruleId).Concat(FailuresFor(BaselineOf(input, context), ruleId)).ToList();
        if (reasons.Count > 0)
        {
            return new CoverageEntry(ruleId, context.ContextId, CoverageStatus.Failed, reasons);
        }

        return new CoverageEntry(ruleId, context.ContextId, assessed ? CoverageStatus.Complete : CoverageStatus.NotApplicable, []);
    }

    private static IEnumerable<Reason> FailuresFor(ContextFacts? context, string ruleId) =>
        context is null
            ? []
            : context.Failures.Where(failure => failure.RuleId is null || failure.RuleId == ruleId)
                .Select(failure => failure with { RuleId = ruleId, ContextId = context.ContextId });
}

public static class ContextIds
{
    /// <summary>Maps "path|framework" or a plain path through Git's rename map; null when the path is new.</summary>
    public static string? ToBaseline(string contextId, ChangeMap map)
    {
        var separator = contextId.IndexOf('|', StringComparison.Ordinal);
        var path = separator < 0 ? contextId : contextId[..separator];
        var baselinePath = map.BaselinePathOf(path);
        return baselinePath is null ? null : separator < 0 ? baselinePath : baselinePath + contextId[separator..];
    }
}
