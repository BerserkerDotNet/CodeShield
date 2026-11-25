namespace CodeShield.Abstractions;

/// <summary>One-based, inclusive source range.</summary>
public sealed record SourceSpan(int StartLine, int StartColumn, int EndLine, int EndColumn)
{
    public bool IntersectsLines(int firstLine, int lastLine) =>
        StartLine <= lastLine && EndLine >= firstLine;
}

/// <summary>A machine-readable reason with optional rule and context scope.</summary>
public sealed record Reason(string Code, string Message, string? RuleId = null, string? ContextId = null);

public static class ReasonCodes
{
    public const string InvalidArgument = "INVALID_ARGUMENT";
    public const string UnsupportedContractVersion = "UNSUPPORTED_CONTRACT_VERSION";
    public const string UnknownRuleset = "UNKNOWN_RULESET";
    public const string InvalidBaseline = "INVALID_BASELINE";
    public const string GitFailure = "GIT_FAILURE";
    public const string MissingAnalyzer = "MISSING_ANALYZER";
    public const string MissingDependency = "MISSING_DEPENDENCY";
    public const string ProjectLoadFailed = "PROJECT_LOAD_FAILED";
    public const string UnresolvedOwnership = "UNRESOLVED_OWNERSHIP";
    public const string SuppressedRule = "SUPPRESSED_RULE";
    public const string StaleSnapshot = "STALE_SNAPSHOT";
    public const string UnreadableSource = "UNREADABLE_SOURCE";
    public const string UnreconciledScope = "UNRECONCILED_SCOPE";
    public const string EvaluationNotFound = "EVALUATION_NOT_FOUND";
    public const string RuleNotFound = "RULE_NOT_FOUND";
    public const string InvalidConfiguration = "INVALID_CONFIGURATION";
}

public static class Languages
{
    public const string CSharp = "csharp";
    public const string TypeScript = "typescript";
}
