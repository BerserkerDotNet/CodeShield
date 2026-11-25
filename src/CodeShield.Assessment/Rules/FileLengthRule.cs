using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Rules;

/// <summary>Flags new oversized files, files crossing the limit, and oversized files that gain a method.</summary>
public sealed class FileLengthRule : ContextRuleBase
{
    public override IReadOnlyList<string> Ids => [RuleIds.FileLength];

    protected override ContextOutcome EvaluateContext(RuleInput input, ContextFacts context)
    {
        var files = context.Files.Where(file => !file.Generated && input.Map.IsChanged(file.Path)).ToList();
        if (files.Count == 0)
        {
            return ContextOutcome.None;
        }

        var rule = input.Ruleset.Get(RuleIds.FileLength);
        var findings = files.Select(file => Evaluate(input, rule, context, file))
            .OfType<RuleFinding>().ToList();
        return new ContextOutcome(findings, new HashSet<string> { RuleIds.FileLength });
    }

    private static RuleFinding? Evaluate(RuleInput input, RuleDefinition rule, ContextFacts context, FileFact file)
    {
        var limit = input.Policy.IntLimit(RuleIds.FileLength, file.Path, (int)rule.Limit!);
        if (file.CodeLines <= limit)
        {
            return null;
        }

        var baselineFile = BaselineFile(input, file);
        var reason = ReasonFor(baselineFile, file, limit);
        return reason is null ? null : FindingFactory.Create(rule, new FindingSpec(
            context.ContextId, Attribution.Introduced, true,
            [FindingFactory.Location(file.Path, new SourceSpan(1, 1, file.CodeLines, 1))],
            file.CodeLines, $"{file.CodeLines} code lines; {reason}",
            [Ev.Item("fileLength", ("codeLines", file.CodeLines), ("baselineCodeLines", baselineFile?.CodeLines), ("reason", reason))], limit));
    }

    private static FileFact? BaselineFile(RuleInput input, FileFact file)
    {
        var baselinePath = input.Map.BaselinePathOf(file.Path);
        return baselinePath is null ? null : input.Baseline.FindFile(baselinePath);
    }

    private static string? ReasonFor(FileFact? baseline, FileFact current, int limit)
    {
        if (baseline is null)
        {
            return "new file above the limit";
        }

        if (baseline.CodeLines <= limit)
        {
            return "existing file crossed the limit";
        }

        var known = baseline.Methods.Select(method => method.UnitId).ToHashSet(StringComparer.Ordinal);
        return current.Methods.Any(method => !known.Contains(method.UnitId)) ? "oversized file gained a method" : null;
    }
}
