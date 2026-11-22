using CodeShield.Abstractions;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

/// <summary>Project/package dependency cycles that contain at least one new edge, evaluated per analysis context.</summary>
public sealed class ModuleCycleRule : GraphRuleBase
{
    public override IReadOnlyList<string> Ids => [RuleIds.ModuleCycle];

    protected override bool Applies(RuleInput input, ContextGraph context) => context.Edges.Count > 0;

    protected override IEnumerable<RuleFinding> Find(RuleInput input, RuleDefinition rule, ContextGraph context)
    {
        var view = new CycleView(context.ContextId, ContextEdges.Of(context, input.Current.Graph, false),
            [.. context.Support.Select(support => (support.Edge.From, support.Edge.To, Owner: support.ContextId))]);
        var baseline = ContextEdges.Baseline(input, context.ContextId, false);
        baseline.UnionWith(ContextEdges.BaselineAll(input, false).Where(edge => view.Support.Any(support => (support.From, support.To) == edge)));
        return CycleFindings(rule, view, baseline, input.Map.IsChanged);
    }
}

/// <summary>TypeScript import cycles inside one package; type-only imports are ignored. Evaluated per tsconfig context.</summary>
public sealed class FileCycleRule : GraphRuleBase
{
    public override IReadOnlyList<string> Ids => [RuleIds.FileCycle];

    protected override bool Applies(RuleInput input, ContextGraph context) => context.FileImports.Count > 0;

    protected override IEnumerable<RuleFinding> Find(RuleInput input, RuleDefinition rule, ContextGraph context)
    {
        var view = new CycleView(context.ContextId, ContextEdges.Of(context, input.Current.Graph, true), []);
        var baseline = ContextEdges.Baseline(input, context.ContextId, true);
        return CycleFindings(rule, view, baseline, input.Map.IsChanged);
    }
}
