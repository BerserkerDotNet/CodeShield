using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Graph;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

/// <summary>Shared behaviour of rules over the repository-wide dependency graph (one "repository" context).</summary>
public abstract class GraphRuleBase : IRule
{
    public const string RepositoryContext = DependencyGraph.RepositoryContext;

    public abstract IReadOnlyList<string> Ids { get; }

    public RuleOutput Evaluate(RuleInput input)
    {
        var output = new RuleOutput();
        var ruleId = Ids[0];
        var failures = input.GraphFailures.Where(failure => failure.RuleId is null || failure.RuleId == ruleId).ToList();
        if (failures.Count > 0)
        {
            output.Coverage.Add(new CoverageEntry(ruleId, RepositoryContext, CoverageStatus.Failed,
                [.. failures.Select(failure => failure with { RuleId = ruleId, ContextId = RepositoryContext })]));
            return output;
        }

        var contexts = ContextEdges.ContextsOf(input.Current.Graph).Where(context => Applies(input, context)).ToList();
        if (contexts.Count == 0)
        {
            output.Coverage.Add(new CoverageEntry(ruleId, RepositoryContext, CoverageStatus.NotApplicable, []));
            return output;
        }

        var rule = input.Ruleset.Get(ruleId);
        foreach (var context in contexts)
        {
            output.Findings.AddRange(Find(input, rule, context));
            output.Coverage.Add(new CoverageEntry(ruleId, context.ContextId, CoverageStatus.Complete, []));
        }

        return output;
    }

    protected abstract bool Applies(RuleInput input, ContextGraph context);

    protected abstract IEnumerable<RuleFinding> Find(RuleInput input, RuleDefinition rule, ContextGraph context);
    protected static RuleFinding CycleFinding(RuleDefinition rule, string contextId, IReadOnlyList<string> members, bool introduced, IReadOnlyList<(string From, string To)> newEdges)
    {
        var attribution = introduced ? Attribution.Introduced : Attribution.PreExisting;
        var spec = new FindingSpec(
            contextId, attribution, introduced,
            [.. members.Select(member => FindingFactory.Location(member, new SourceSpan(1, 1, 1, 1)))],
            members.Count, $"cycle of {members.Count} members",
            [
                Ev.Item("cycle", ("members", members)),
                Ev.Item("newEdges", ("edges", newEdges.Select(edge => new { edge.From, edge.To }).ToList())),
            ]);
        return FindingFactory.Create(rule, spec);
    }

    /// <summary>
    /// Emits one finding per cycle that contains an edge owned by the view's context: charged when it contains a new edge,
    /// context-only when touched but old. A cycle that also runs through another context's support edges is reported only by
    /// the first such context, so coexisting contexts do not charge the same cycle twice.
    /// </summary>
    protected static IEnumerable<RuleFinding> CycleFindings(
        RuleDefinition rule,
        CycleView view,
        ISet<(string From, string To)> baselineEdges,
        Func<string, bool> isTouched)
    {
        var edges = view.Own.Concat(view.Support.Select(support => (support.From, support.To))).Distinct().ToList();
        var nodes = edges.SelectMany(edge => new[] { edge.From, edge.To }).Distinct().ToList();
        foreach (var members in StronglyConnected.Cycles(nodes, edges))
        {
            var inside = members.ToHashSet(StringComparer.Ordinal);
            var cycle = edges.Where(edge => inside.Contains(edge.From) && inside.Contains(edge.To)).ToList();
            if (!view.Reports(cycle))
            {
                continue;
            }

            var added = cycle.Where(edge => !baselineEdges.Contains(edge)).ToList();
            if (added.Count > 0 || members.Any(isTouched))
            {
                yield return CycleFinding(rule, view.ContextId, members, added.Count > 0, added);
            }
        }
    }
}

/// <summary>The edges one context sees: its own (chargeable) edges and the supporting edges of coexisting contexts.</summary>
public sealed record CycleView(
    string ContextId,
    IReadOnlyList<(string From, string To)> Own,
    IReadOnlyList<(string From, string To, string Owner)> Support)
{
    /// <summary>True when the cycle uses an own edge and no coexisting context with a smaller id owns a support edge of it.</summary>
    public bool Reports(IReadOnlyList<(string From, string To)> cycle)
    {
        var own = Own.ToHashSet();
        if (!cycle.Any(own.Contains))
        {
            return false;
        }

        return !Support.Any(support => !own.Contains((support.From, support.To))
            && cycle.Contains((support.From, support.To))
            && string.CompareOrdinal(support.Owner, ContextId) < 0);
    }
}
