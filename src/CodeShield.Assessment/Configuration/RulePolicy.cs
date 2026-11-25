namespace CodeShield.Core.Configuration;

/// <summary>
/// The cascading effective policy: for each setting the nearest enclosing folder that sets it wins. Paths are
/// repository-relative with '/', compared ordinally, with segment-boundary prefix matching.
/// </summary>
public sealed class RulePolicy(IReadOnlyList<FolderConfig> configs)
{
    public static RulePolicy Defaults { get; } = new([]);

    public IReadOnlyList<FolderConfig> Configs => configs;

    public bool IsExcluded(string ruleId, string path)
    {
        var applicable = Applicable(path);
        var exclude = applicable.Select(config => config.Exclude).LastOrDefault(value => value is not null);
        var enabled = applicable.Select(config => Setting(config, ruleId)?.Enabled).LastOrDefault(value => value is not null);
        return exclude == true || enabled == false;
    }

    /// <summary>True when the whole folder subtree containing <paramref name="path"/> is excluded from measurement.</summary>
    public bool IsFullyExcluded(string path) =>
        Applicable(path).Select(config => config.Exclude).LastOrDefault(value => value is not null) == true;

    /// <summary>True when the rule is excluded at the repository root and in every configured folder, so everywhere.</summary>
    public bool ExcludedEverywhere(string ruleId) =>
        configs.Count > 0 && Directories().All(directory => IsExcluded(ruleId, directory));

    public int IntLimit(string ruleId, string path, int fallback) =>
        configs.Count == 0 ? fallback : (int)(LimitAt(ruleId, path) ?? fallback);

    public decimal DecimalLimit(string ruleId, string path, decimal fallback) =>
        configs.Count == 0 ? fallback : LimitAt(ruleId, path) ?? fallback;

    private decimal? LimitAt(string ruleId, string path) =>
        Applicable(path).Select(config => Setting(config, ruleId)?.Limit).LastOrDefault(value => value is not null);

    private IEnumerable<string> Directories() =>
        configs.Select(config => config.Directory).Append(string.Empty).Distinct(StringComparer.Ordinal);

    private List<FolderConfig> Applicable(string path) =>
        [.. configs.Where(config => Covers(config.Directory, path)).OrderBy(config => config.Directory.Length)];

    private static RuleSetting? Setting(FolderConfig config, string ruleId) =>
        config.Rules.TryGetValue(ruleId, out var setting) ? setting : null;

    private static bool Covers(string directory, string path) =>
        directory.Length == 0
        || string.Equals(directory, path, StringComparison.Ordinal)
        || path.StartsWith(directory + "/", StringComparison.Ordinal);
}
