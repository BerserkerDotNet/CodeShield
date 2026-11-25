namespace CodeShield.Core.Contracts;

public static class ConfigChange
{
    public const string Unchanged = "unchanged";
}

public sealed record ConfigFileDto(string Path, string? Sha256, string Change);

public sealed record ConfigExclusionDto(string Path, string Reason, string Source);

public sealed record ConfigOverrideDto(string Path, string RuleId, bool? Enabled, decimal? Limit, string? Reason, string Source);

public sealed record ExcludedFindingsDto(string RuleId, int Count);

/// <summary>Discloses the configuration that shaped a result; present only when a configuration file exists or changed.</summary>
public sealed record ConfigurationDisclosure(
    IReadOnlyList<ConfigFileDto> Files,
    bool Changed,
    IReadOnlyList<ConfigExclusionDto> Exclusions,
    IReadOnlyList<ConfigOverrideDto> Overrides,
    IReadOnlyList<ExcludedFindingsDto> ExcludedFindings);
