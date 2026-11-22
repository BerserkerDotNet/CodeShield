namespace CodeShield.Core.Rulesets;

public static class RuleIds
{
    public const string Cyclomatic = "complexity.cyclomatic";
    public const string Nesting = "complexity.nesting";
    public const string MethodLength = "complexity.method-length";
    public const string Lcom4 = "cohesion.lcom4";
    public const string ModuleCycle = "coupling.module-cycle";
    public const string FileCycle = "coupling.typescript-file-cycle";
    public const string Boundary = "coupling.boundary";
    public const string MainSequence = "coupling.main-sequence-distance";
    public const string FileLength = "structure.file-length";
    public const string Duplication = "structure.duplication";
    public const string ParameterCount = "readability.parameter-count";
}

public sealed record RuleDefinition(
    string Id,
    string Category,
    string Definition,
    string Intent,
    object? Limit,
    int Deduction,
    string Calculation);

public sealed record Ruleset(string Name, string Version, int Threshold, IReadOnlyList<RuleDefinition> Rules)
{
    public string Qualified => $"{Name}@{Version}";

    public RuleDefinition? Find(string ruleId) =>
        Rules.FirstOrDefault(rule => string.Equals(rule.Id, ruleId, StringComparison.Ordinal));

    public RuleDefinition Get(string ruleId) =>
        Find(ruleId) ?? throw new InvalidOperationException($"Rule '{ruleId}' is not part of ruleset {Qualified}.");
}
