using System.ComponentModel;
using CodeShield.Core.Contracts;
using CodeShield.Core.Evaluation;
using ModelContextProtocol.Server;

namespace CodeShield.Mcp;

[McpServerToolType]
public sealed class QualityTools(EvaluationService evaluation, RuleDetailsService details)
{
    [McpServerTool(Name = "evaluate_quality", Title = "Evaluate code quality", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Scores the C#, TypeScript and Kotlin changes in a Git workspace since a baseline revision against deterministic quality rules. Returns a score out of 100, pass/fail against the threshold, findings and analysis coverage.")]
    public Task<EvaluationResponse> EvaluateQuality(
        [Description("Contract version; must be \"1\".")] string contractVersion,
        [Description("Absolute path of a directory inside the Git repository to evaluate.")] string workspace,
        [Description("Git revision to compare against, for example \"HEAD\" or a commit ID.")] string baseline,
        [Description("Ruleset name; defaults to \"default\".")] string? ruleset = null,
        CancellationToken cancellationToken = default) =>
        evaluation.EvaluateAsync(new EvaluateRequest(contractVersion, workspace, baseline, ruleset), cancellationToken);

    [McpServerTool(Name = "get_rule_details", Title = "Get rule details", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Returns the rule definition and every finding of that rule, with calculations and evidence, from a previous evaluation. Details are kept in memory only.")]
    public RuleDetailsResponse GetRuleDetails(
        [Description("Contract version; must be \"1\".")] string contractVersion,
        [Description("The evaluationId returned by evaluate_quality.")] string evaluationId,
        [Description("The rule ID, for example \"complexity.cyclomatic\".")] string ruleId) =>
        details.Get(new RuleDetailsRequest(contractVersion, evaluationId, ruleId));
}
