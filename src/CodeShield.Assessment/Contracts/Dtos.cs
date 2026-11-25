using System.Text.Json.Serialization;
using CodeShield.Abstractions;

namespace CodeShield.Core.Contracts;

public sealed record EvaluateRequest(string? ContractVersion, string? Workspace, string? Baseline, string? Ruleset);

public sealed record FindingLocation(string File, int StartLine, int StartColumn, int EndLine, int EndColumn);

public sealed record FindingDto(
    string RuleId,
    string Definition,
    string Intent,
    string ContextId,
    string Attribution,
    int Deduction,
    IReadOnlyList<FindingLocation> Locations,
    object? Actual,
    object? Limit,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Evidence);

public sealed record CategoryScore(string Name, int Deduction);

public sealed record CoverageEntry(string RuleId, string ContextId, string Status, IReadOnlyList<Reason> Reasons);

public sealed record ScopeEntry(string Path, string Status);

public sealed record EvaluationResponse(
    string ContractVersion,
    string Status,
    string? EvaluationId,
    string? BaselineCommit,
    string? SnapshotId,
    string? Ruleset,
    string? RulesetVersion,
    IReadOnlyDictionary<string, string> AnalyzerVersions,
    int? Score,
    int Threshold,
    bool? Passed,
    IReadOnlyList<CategoryScore> Categories,
    IReadOnlyList<FindingDto> Findings,
    IReadOnlyList<CoverageEntry> Coverage,
    IReadOnlyList<Reason> Reasons,
    IReadOnlyList<ScopeEntry> Scope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ConfigurationDisclosure? Configuration = null);

public sealed record RuleDetailsRequest(string? ContractVersion, string? EvaluationId, string? RuleId);

public sealed record RuleDetailFindingDto(
    string ContextId,
    string Attribution,
    IReadOnlyList<FindingLocation> Locations,
    object? Actual,
    object? Limit,
    int Deduction,
    string Calculation,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Evidence);

public sealed record RuleDetailsResponse(
    string ContractVersion,
    string Status,
    string? EvaluationId,
    string? RuleId,
    string? RulesetVersion,
    string? Definition,
    string? Intent,
    object? Limit,
    int? DeductionPerFinding,
    IReadOnlyList<RuleDetailFindingDto> Findings,
    IReadOnlyList<Reason> Reasons);
