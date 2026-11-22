using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

using EvidenceItem = IReadOnlyDictionary<string, object?>;

public static class Ev
{
    /// <summary>Builds an evidence object with <c>kind</c> first.</summary>
    public static EvidenceItem Item(string kind, params (string Key, object? Value)[] fields)
    {
        var item = new Dictionary<string, object?> { ["kind"] = kind };
        foreach (var (key, value) in fields)
        {
            item[key] = value;
        }

        return item;
    }
}

public static class FindingFactory
{
    public static FindingLocation Location(string file, SourceSpan span) =>
        new(file, span.StartLine, span.StartColumn, span.EndLine, span.EndColumn);

    public static RuleFinding Create(RuleDefinition rule, FindingSpec spec) => new(
        rule, spec.ContextId, spec.Attribution, spec.Charged ? rule.Deduction : 0, spec.Locations,
        spec.Actual, spec.Limit ?? rule.Limit, spec.Calculation, spec.Evidence);
}

/// <summary>Inputs for building one finding; deduction is the rule's fixed cost when <c>Charged</c>.</summary>
public sealed record FindingSpec(
    string ContextId,
    string Attribution,
    bool Charged,
    IReadOnlyList<FindingLocation> Locations,
    object? Actual,
    string Calculation,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Evidence,
    object? Limit = null);
