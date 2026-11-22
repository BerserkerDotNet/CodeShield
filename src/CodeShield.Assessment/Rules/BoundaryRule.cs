using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

/// <summary>Forbidden module dependencies declared in .quality-architecture.json.</summary>
public sealed class BoundaryRule : GraphRuleBase
{
    public override IReadOnlyList<string> Ids => [RuleIds.Boundary];

    protected override bool Applies(RuleInput input, ContextGraph context) => input.Architecture is { Error: null } && context.Edges.Count > 0;

    protected override IEnumerable<RuleFinding> Find(RuleInput input, RuleDefinition rule, ContextGraph context)
    {
        var forbidden = input.Architecture is { Error: null } architecture ? architecture.Forbidden : [];
        var baseline = ContextEdges.Baseline(input, context.ContextId, false);
        return context.Edges.SelectMany(edge => forbidden
                .Where(item => Matches(item, edge))
                .Select(item => ToFinding(rule, context.ContextId, edge, item, !baseline.Contains((edge.From, edge.To)))));
    }

    private static bool Matches(ArchitectureRule rule, ModuleEdge edge) =>
        string.Equals(Normalize(rule.From), edge.From, StringComparison.Ordinal)
        && string.Equals(Normalize(rule.To), edge.To, StringComparison.Ordinal);

    public static string Normalize(string path) => path.Replace('\\', '/');

    private static RuleFinding ToFinding(RuleDefinition rule, string contextId, ModuleEdge edge, ArchitectureRule item, bool introduced) =>
        FindingFactory.Create(rule, new FindingSpec(
            contextId, introduced ? Attribution.Introduced : Attribution.PreExisting, introduced,
            [FindingFactory.Location(edge.From, new SourceSpan(1, 1, 1, 1))],
            1, $"{edge.From} depends on forbidden {edge.To}",
            [Ev.Item("forbiddenDependency", ("from", item.From), ("to", item.To))]));
}

