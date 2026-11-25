namespace CodeShield.Core.Configuration;

/// <summary>Raised when a <c>.codeshield.json</c> cannot be read or is invalid; the message names the file and the problem.</summary>
public sealed class ConfigurationException(string message) : Exception(message);

/// <summary>Per-rule settings of one folder; null members are not set by that folder.</summary>
public sealed record RuleSetting(bool? Enabled, decimal? Limit, string? Reason);

/// <summary>One validated <c>.codeshield.json</c>. <c>Directory</c> is repository-relative with '/' and empty for the repository root.</summary>
public sealed record FolderConfig(
    string Path,
    string Directory,
    string Sha256,
    bool? Exclude,
    string? Reason,
    IReadOnlyDictionary<string, RuleSetting> Rules);
