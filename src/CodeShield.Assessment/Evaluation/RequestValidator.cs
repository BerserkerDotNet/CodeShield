using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Evaluation;

public sealed record ValidatedRequest(string Workspace, string Baseline, Ruleset Ruleset);

public sealed record ValidationResult(ValidatedRequest? Request, EvaluationResponse? Error);

public static class RequestValidator
{
    public static ValidationResult Validate(EvaluateRequest request)
    {
        var problem = FirstProblem(request);
        if (problem is not null)
        {
            return new ValidationResult(null, ResponseAssembler.Failure(EvaluationStatus.Error, DefaultRuleset.Threshold, [problem]));
        }

        var ruleset = RulesetCatalog.Resolve(request.Ruleset);
        return new ValidationResult(new ValidatedRequest(request.Workspace!, request.Baseline!, ruleset!), null);
    }

    private static Reason? FirstProblem(EvaluateRequest request)
    {
        if (request.ContractVersion != ResponseAssembler.ContractVersion)
        {
            return new Reason(ReasonCodes.UnsupportedContractVersion,
                $"Contract version '{request.ContractVersion}' is not supported; this server implements '{ResponseAssembler.ContractVersion}'.");
        }

        if (string.IsNullOrWhiteSpace(request.Workspace) || !Directory.Exists(request.Workspace))
        {
            return new Reason(ReasonCodes.InvalidArgument, "'workspace' must be an existing directory.");
        }

        if (string.IsNullOrWhiteSpace(request.Baseline))
        {
            return new Reason(ReasonCodes.InvalidArgument, "'baseline' is required, for example \"HEAD\".");
        }

        return RulesetCatalog.Resolve(request.Ruleset) is null
            ? new Reason(ReasonCodes.UnknownRuleset, $"Ruleset '{request.Ruleset}' is not available.")
            : null;
    }
}
