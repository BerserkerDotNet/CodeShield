using System.Globalization;
using CodeShield.Core.Rulesets;

namespace CodeShield.Core.Configuration;

/// <summary>The rules whose limit may be configured and the values each accepts.</summary>
public sealed record LimitDomain(bool WholeNumber, decimal Minimum, decimal Maximum, string Description)
{
    private static readonly Dictionary<string, LimitDomain> Domains = new(StringComparer.Ordinal)
    {
        [RuleIds.Cyclomatic] = Whole(1, "an integer between 1 and 2147483647"),
        [RuleIds.Nesting] = Whole(0, "an integer between 0 and 2147483647"),
        [RuleIds.MethodLength] = Whole(1, "an integer between 1 and 2147483647"),
        [RuleIds.ParameterCount] = Whole(0, "an integer between 0 and 2147483647"),
        [RuleIds.Lcom4] = Whole(1, "an integer between 1 and 2147483647"),
        [RuleIds.FileLength] = Whole(1, "an integer between 1 and 2147483647"),
        [RuleIds.MainSequence] = new(false, 0m, 1m, "a number between 0 and 1"),
    };

    public static LimitDomain? For(string ruleId) => Domains.GetValueOrDefault(ruleId);

    public bool Accepts(decimal value) =>
        value >= Minimum && value <= Maximum && (!WholeNumber || value == decimal.Truncate(value));

    /// <summary>True when the limit is looser (greater) than the ruleset default and so needs a documented reason.</summary>
    public static bool IsLooserThanDefault(decimal value, object? defaultLimit) =>
        defaultLimit is not null && value > Convert.ToDecimal(defaultLimit, CultureInfo.InvariantCulture);

    private static LimitDomain Whole(decimal minimum, string description) => new(true, minimum, int.MaxValue, description);
}
