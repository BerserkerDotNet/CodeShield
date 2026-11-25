namespace CodeShield.Core.Contracts;

public static class Attribution
{
    public const string Introduced = "introduced";
    public const string PreExisting = "pre_existing";
    public const string Ambiguous = "ambiguous";
}

public static class EvaluationStatus
{
    public const string Complete = "complete";
    public const string Incomplete = "incomplete";
    public const string NoChanges = "no_changes";
    public const string Error = "error";
}

public static class CoverageStatus
{
    public const string Complete = "complete";
    public const string NotApplicable = "not_applicable";
    public const string Failed = "failed";
    public const string Excluded = "excluded";
}
