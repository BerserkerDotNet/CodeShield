using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

/// <summary>LCOM4 for new or modified classes; a pre-existing violation is still charged but labelled as such.</summary>
public sealed class CohesionRule : ContextRuleBase
{
    public override IReadOnlyList<string> Ids => [RuleIds.Lcom4];

    protected override ContextOutcome EvaluateContext(RuleInput input, ContextFacts context)
    {
        var affected = context.Classes.Where(@class => @class.Applicable && IsAffected(input, @class)).ToList();
        if (affected.Count == 0)
        {
            return ContextOutcome.None;
        }

        var rule = input.Ruleset.Get(RuleIds.Lcom4);
        var baseline = new BaselineContext(input.Map, BaselineOf(input, context));
        var findings = affected.Where(@class => @class.Components.Count >= 2)
            .Select(@class => ToFinding(input, rule, context, (@class, baseline)))
            .ToList();
        return new ContextOutcome(findings, new HashSet<string> { RuleIds.Lcom4 });
    }

    private static bool IsAffected(RuleInput input, ClassFact @class) =>
        @class.Locations.Any(location => input.Map.IsSpanAffected(location.File, location.Span));

    private static RuleFinding ToFinding(
        RuleInput input, RuleDefinition rule, ContextFacts context, (ClassFact Class, BaselineContext Baseline) item)
    {
        var @class = item.Class;
        var attribution = AttributionOf(input, @class, item.Baseline);
        var evidence = new List<IReadOnlyDictionary<string, object?>>
        {
            Ev.Item("components", ("count", @class.Components.Count), ("groups", @class.Components)),
            Ev.Item("nodes", ("nodes", @class.Nodes.Select(node => new { id = node.Id, name = node.Name }).ToList())),
            Ev.Item("edges", ("edges", @class.Edges.Select(edge => new { edge.From, edge.To, edge.Reason }).ToList())),
            Ev.Item("attribution", ("baselineComponents", BaselineComponents(input, @class, item.Baseline))),
        };
        return FindingFactory.Create(rule, new FindingSpec(
            context.ContextId, attribution, true,
            [.. @class.Locations.Select(location => FindingFactory.Location(location.File, location.Span))],
            @class.Components.Count, $"{@class.Components.Count} connected components of {@class.Nodes.Count} members", evidence));
    }

    private static string AttributionOf(RuleInput input, ClassFact @class, BaselineContext baseline) =>
        BaselineComponents(input, @class, baseline) >= 2 ? Attribution.PreExisting : Attribution.Introduced;

    private static int? BaselineComponents(RuleInput input, ClassFact @class, BaselineContext baseline)
    {
        var match = baseline.ClassOf(@class.UnitId);
        return match is { Applicable: true } ? match.Components.Count : null;
    }
}

