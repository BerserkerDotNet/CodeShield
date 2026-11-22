using CodeShield.Abstractions;
using CodeShield.Core.Contracts;

namespace CodeShield.Core.Evaluation;

/// <summary>Implements <c>get_rule_details</c> against the stored evaluation, never the current workspace.</summary>
public sealed class RuleDetailsService(EvaluationStore store)
{
    public RuleDetailsResponse Get(RuleDetailsRequest request)
    {
        if (request.ContractVersion != ResponseAssembler.ContractVersion)
        {
            return Error(request, ReasonCodes.UnsupportedContractVersion,
                $"Contract version '{request.ContractVersion}' is not supported; this server implements '{ResponseAssembler.ContractVersion}'.");
        }

        var evaluation = string.IsNullOrWhiteSpace(request.EvaluationId) ? null : store.Find(request.EvaluationId);
        if (evaluation is null)
        {
            return Error(request, ReasonCodes.EvaluationNotFound,
                "The evaluation ID is unknown or expired; details are kept in memory only. Re-run evaluate_quality.");
        }

        var matching = evaluation.Findings.Where(finding => finding.Rule.Id == request.RuleId).ToList();
        if (matching.Count == 0)
        {
            return Error(request, ReasonCodes.RuleNotFound, $"The evaluation has no findings for rule '{request.RuleId}'.");
        }

        var rule = matching[0].Rule;
        return new RuleDetailsResponse(
            ResponseAssembler.ContractVersion, "complete", evaluation.EvaluationId, rule.Id, evaluation.Ruleset.Version,
            rule.Definition, rule.Intent, rule.Limit, rule.Deduction, [.. matching.Select(ResponseAssembler.ToDetail)], []);
    }

    private static RuleDetailsResponse Error(RuleDetailsRequest request, string code, string message) => new(
        ResponseAssembler.ContractVersion, "error", request.EvaluationId, request.RuleId, null, null, null, null, null,
        [], [new Reason(code, message)]);
}
