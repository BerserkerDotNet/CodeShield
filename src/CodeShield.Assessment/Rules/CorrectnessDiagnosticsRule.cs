using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

/// <summary>Pinned analyzer diagnostics in changed files, attributed against the baseline snapshot.</summary>
public sealed class CorrectnessDiagnosticsRule(ICorrectnessSelection selection) : ContextRuleBase
{
    public override IReadOnlyList<string> Ids => [.. selection.All.Select(rule => rule.EvaluatorId)];

    protected override IReadOnlyList<string> IdsFor(ContextFacts context) =>
        [.. selection.ForLanguage(context.Language).Select(rule => rule.EvaluatorId)];

    protected override ContextOutcome EvaluateContext(RuleInput input, ContextFacts context)
    {
        var baseline = BaselineOf(input, context);
        var findings = new List<RuleFinding>();
        foreach (var diagnostic in context.Diagnostics.Where(item => input.Map.IsChanged(item.File)))
        {
            var catalogRule = selection.FindByAnalyzerId(context.Language, diagnostic.AnalyzerId);
            if (catalogRule is not null)
            {
                findings.Add(ToFinding(input, context, baseline, (catalogRule, diagnostic)));
            }
        }

        var assessed = IdsFor(context).Where(_ => context.OwnedFiles.Any(input.Map.IsChanged)).ToHashSet();
        return new ContextOutcome(findings, assessed);
    }

    private static RuleFinding ToFinding(
        RuleInput input, ContextFacts context, ContextFacts? baseline, (CorrectnessRule Rule, DiagnosticFact Diagnostic) item)
    {
        var diagnostic = item.Diagnostic;
        var attribution = Attribute(input, baseline, diagnostic);
        var definition = input.Ruleset.Get(item.Rule.EvaluatorId);
        return FindingFactory.Create(definition, new FindingSpec(
            context.ContextId, attribution, attribution != Attribution.PreExisting,
            [FindingFactory.Location(diagnostic.File, diagnostic.Span)],
            1, diagnostic.Message,
            [Ev.Item("diagnostic", ("analyzerId", diagnostic.AnalyzerId), ("message", diagnostic.Message), ("unitId", diagnostic.UnitId))]));
    }

    private static string Attribute(RuleInput input, ContextFacts? baseline, DiagnosticFact diagnostic)
    {
        var baselineFile = input.Map.BaselinePathOf(diagnostic.File);
        if (baselineFile is null)
        {
            return Attribution.Introduced;
        }

        var baselineFacts = new BaselineContext(input.Map, baseline).FileOf(diagnostic.File);
        if (baseline is null || baselineFacts is null)
        {
            return Attribution.Ambiguous;
        }

        if (IsNewUnit(new BaselineContext(input.Map, baseline), baselineFacts, diagnostic))
        {
            return Attribution.Introduced;
        }

        var sameRule = baseline.Diagnostics.Where(old => old.AnalyzerId == diagnostic.AnalyzerId && old.File == baselineFile
            && old.UnitId == UnitIds.ToBaseline(diagnostic.UnitId, input.Map)).ToList();
        return Classify(input, diagnostic, sameRule);
    }

    private static string Classify(RuleInput input, DiagnosticFact diagnostic, List<DiagnosticFact> sameRule)
    {
        var line = input.Map.MapLineToBaseline(diagnostic.File, diagnostic.Span.StartLine);
        if (line is null)
        {
            return sameRule.Count > 0 ? Attribution.Ambiguous : Attribution.Introduced;
        }

        var matches = sameRule.Count(old => old.Span.StartLine == line);
        return matches switch
        {
            0 => Attribution.Introduced,
            1 => Attribution.PreExisting,
            _ => Attribution.Ambiguous,
        };
    }

    private static bool IsNewUnit(BaselineContext baseline, FileFact baselineFile, DiagnosticFact diagnostic)
    {
        if (string.IsNullOrEmpty(diagnostic.UnitId))
        {
            return false;
        }

        return baseline.LacksUnit(baselineFile, diagnostic.UnitId);
    }
}



