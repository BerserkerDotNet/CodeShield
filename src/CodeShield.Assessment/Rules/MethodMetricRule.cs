using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

/// <summary>Complexity, nesting, method length and parameter count for new or modified methods.</summary>
public sealed class MethodMetricRule : ContextRuleBase
{
    private sealed record Metric(string RuleId, Func<MethodFact, int> Measure, Func<MethodFact, string> Calculate, Func<MethodFact, IReadOnlyList<IReadOnlyDictionary<string, object?>>> Evidence);

    private static readonly Metric[] Metrics =
    [
        new(RuleIds.Cyclomatic, method => method.Complexity,
            method => $"1 base + {method.Complexity - 1} decisions", ComplexityEvidence),
        new(RuleIds.Nesting, method => method.Nesting,
            method => $"maximum nesting depth {method.Nesting}", NestingEvidence),
        new(RuleIds.MethodLength, method => method.CodeLines,
            method => $"{method.CodeLines} code lines", method => [Ev.Item("codeLines", ("count", method.CodeLines))]),
        new(RuleIds.ParameterCount, method => method.ParameterCount,
            method => $"{method.ParameterCount} declared parameters",
            method => [Ev.Item("parameters", ("count", method.ParameterCount))]),
    ];

    public override IReadOnlyList<string> Ids => [.. Metrics.Select(metric => metric.RuleId)];

    protected override ContextOutcome EvaluateContext(RuleInput input, ContextFacts context)
    {
        var affected = AffectedMethods(input, context).ToList();
        if (affected.Count == 0)
        {
            return ContextOutcome.None;
        }

        var attributor = new MethodAttributor(input.Map, new BaselineContext(input.Map, BaselineOf(input, context)));
        var findings = new List<RuleFinding>();
        foreach (var metric in Metrics)
        {
            var rule = input.Ruleset.Get(metric.RuleId);
            var limit = (int)rule.Limit!;
            findings.AddRange(affected.Where(item => metric.Measure(item.Method) > limit)
                .Select(item => ToFinding(rule, metric, context, item, attributor.Classify(item.File, item.Method, metric.Measure, limit))));
        }

        return new ContextOutcome(findings, new HashSet<string>(Ids));
    }

    private static IEnumerable<(string File, MethodFact Method)> AffectedMethods(RuleInput input, ContextFacts context) =>
        context.Files.Where(file => !file.Generated)
            .SelectMany(file => file.Methods.Select(method => (file.Path, method)))
            .Where(item => input.Map.IsSpanAffected(item.Path, item.method.Span));

    private static RuleFinding ToFinding(
        RuleDefinition rule, Metric metric, ContextFacts context, (string File, MethodFact Method) item, string attribution)
    {
        var actual = metric.Measure(item.Method);
        var evidence = new List<IReadOnlyDictionary<string, object?>>
        {
            Ev.Item("method", ("name", item.Method.Name), ("unitId", item.Method.UnitId)),
        };
        evidence.AddRange(metric.Evidence(item.Method));
        return FindingFactory.Create(rule, new FindingSpec(
            context.ContextId, attribution, true,
            [FindingFactory.Location(item.File, item.Method.Span)], actual, metric.Calculate(item.Method), evidence));
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ComplexityEvidence(MethodFact method) =>
    [
        Ev.Item("decisionCount", ("base", 1), ("decisions", method.Complexity - 1)),
        .. method.Decisions.Select(decision => Ev.Item("decision", ("decisionKind", decision.DecisionKind), ("line", decision.Line))),
    ];

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> NestingEvidence(MethodFact method) =>
    [
        Ev.Item("maxDepth", ("depth", method.Nesting)),
        .. method.NestingPoints.Select(point => Ev.Item(
            "nesting", ("constructKind", point.ConstructKind), ("line", point.Line), ("depth", point.Depth))),
    ];
}

