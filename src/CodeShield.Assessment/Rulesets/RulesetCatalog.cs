namespace CodeShield.Core.Rulesets;

public static class RulesetCatalog
{
    private static readonly Ruleset DefaultInstance = DefaultRuleset.Create();

    public static Ruleset Default => DefaultInstance;

    /// <summary>Resolves "default" or "default@version"; null when unknown. An empty value selects the default.</summary>
    public static Ruleset? Resolve(string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return DefaultInstance;
        }

        var parts = requested.Split('@', 2);
        return Matches(parts[0], DefaultInstance.Name) && (parts.Length == 1 || Matches(parts[1], DefaultInstance.Version))
            ? DefaultInstance
            : null;
    }

    private static bool Matches(string actual, string expected) => string.Equals(actual, expected, StringComparison.Ordinal);
}
