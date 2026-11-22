using CodeShield.Abstractions;
using CodeShield.Core.Graph;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

/// <summary>Distance from the main sequence, D = |A + I - 1|, per analysis context. Ce counts the context's own dependencies; Ca counts distinct dependents in any context.</summary>
public sealed class MainSequenceRule : ContextRuleBase
{
    public override IReadOnlyList<string> Ids => [RuleIds.MainSequence];

    protected override ContextOutcome EvaluateContext(RuleInput input, ContextFacts context)
    {
        var current = context.ModulePath is null ? null : Distance(context, ContextEdges.GraphKey(context.ContextId), input.Current.Graph);
        if (current is null)
        {
            return ContextOutcome.None;
        }

        var baseline = BaselineDistance(input, context);
        if (!IsAffected(input, context, current, baseline))
        {
            return ContextOutcome.None;
        }

        var rule = input.Ruleset.Get(RuleIds.MainSequence);
        var assessed = new HashSet<string> { RuleIds.MainSequence };
        return IsViolation(current, baseline, Convert.ToDecimal(rule.Limit ?? 0.5m, System.Globalization.CultureInfo.InvariantCulture))
            ? new ContextOutcome([ToFinding(rule, context, current, baseline)], assessed)
            : new ContextOutcome([], assessed);
    }

    /// <summary>Source edits, a changed module manifest, or a changed dependency set all make a module affected.</summary>
    private static bool IsAffected(RuleInput input, ContextFacts context, Measure current, Measure? baseline) =>
        context.OwnedFiles.Any(input.Map.IsChanged)
        || input.Map.IsChanged(context.ModulePath!)
        || baseline is null
        || baseline.Ca != current.Ca
        || baseline.Ce != current.Ce;

    /// <summary>Exact rational comparison: D &gt; limit and D - baseline D &gt;= 1/10, with no floating-point rounding.</summary>
    private static bool IsViolation(Measure current, Measure? baseline, decimal limit) =>
        current.Numerator > limit * current.Denominator
        && (baseline is null || 10 * (current.Numerator * baseline.Denominator - baseline.Numerator * current.Denominator) >= current.Denominator * baseline.Denominator);

    private static Measure? BaselineDistance(RuleInput input, ContextFacts context)
    {
        var baseline = BaselineOf(input, context);
        var modulePath = context.ModulePath is null ? null : input.Map.BaselinePathOf(context.ModulePath);
        return baseline is null || modulePath is null ? null : Distance(baseline with { ModulePath = modulePath }, ContextEdges.BaselineId(input.Map, ContextEdges.GraphKey(context.ContextId)), input.Baseline.Graph);
    }

    private static Measure? Distance(ContextFacts context, string contextId, ModuleGraphFacts graph)
    {
        var module = context.ModulePath!;
        var dependencies = new DependencyGraph(graph);
        var ca = dependencies.Afferent(module);
        var ce = dependencies.Efferent(contextId, module);
        var types = context.Types.Count;
        return types == 0 || ca + ce == 0 ? null : new Measure(context.Types.Count(type => type.IsAbstract), types, ca, ce);
    }

    private static RuleFinding ToFinding(RuleDefinition rule, ContextFacts context, Measure current, Measure? baseline) =>
        FindingFactory.Create(rule, new FindingSpec(
            context.ContextId, Contracts.Attribution.Introduced, true,
            [FindingFactory.Location(context.ModulePath!, new SourceSpan(1, 1, 1, 1))],
            Math.Round(current.D, 3), $"D = |A + I - 1| = |{current.A:0.###} + {current.I:0.###} - 1|",
            [Ev.Item("mainSequence", ("abstractness", current.A), ("instability", current.I),
                ("afferent", current.Ca), ("efferent", current.Ce), ("eligibleTypes", current.Types),
                ("abstractTypes", current.Abstract), ("distance", current.D), ("baselineDistance", baseline?.D),
                ("baselineEligibleTypes", baseline?.Types), ("baselineAbstractTypes", baseline?.Abstract),
                ("baselineAfferent", baseline?.Ca), ("baselineEfferent", baseline?.Ce))]));

    /// <summary>D = |A*(Ca+Ce) + Ce*T - T*(Ca+Ce)| / (T*(Ca+Ce)) from integer counts: abstract types, all types, Ca, Ce.</summary>
    private sealed record Measure(int Abstract, int Types, int Ca, int Ce)
    {
        public long Denominator => (long)Types * (Ca + Ce);

        public long Numerator => Math.Abs((long)Abstract * (Ca + Ce) + (long)Ce * Types - Denominator);

        public double A => (double)Abstract / Types;

        public double I => (double)Ce / (Ca + Ce);

        public double D => (double)Numerator / Denominator;
    }
}