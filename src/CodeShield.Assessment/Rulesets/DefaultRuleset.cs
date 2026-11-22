using CodeShield.Abstractions;

namespace CodeShield.Core.Rulesets;

/// <summary>The versioned default ruleset (v1.0.0): fixed limits and per-violation deductions.</summary>
public static class DefaultRuleset
{
    public const string Name = "default";
    public const string Version = "1.0.0";
    public const int Threshold = 90;
    public const int CorrectnessDeduction = 12;

    public static Ruleset Create() => new(Name, Version, Threshold, [.. StructuralRules(), .. CorrectnessRules()]);

    private static IEnumerable<RuleDefinition> StructuralRules() =>
    [
        .. ComplexityRules(),
        new(RuleIds.Lcom4, "cohesion",
            "LCOM4 is the number of connected components of an applicable class's instance members linked by shared instance state or direct calls.",
            "Identify classes that bundle unrelated responsibilities.", 1, 12,
            "components of the member graph; flagged when 2 or more"),
        .. CouplingRules(),
        new(RuleIds.FileLength, "structure",
            "Code lines in a file; a new file above the limit, an existing file crossing it, or an oversized file that gains a method is flagged.",
            "Keep files small enough to navigate and review.", 400, 11, "code lines in the file"),
        new(RuleIds.Duplication, "structure",
            "Exact token-sequence matches of at least 10 code lines and 50 tokens where at least one occurrence is new or changed.",
            "Avoid copy-pasted blocks that must be maintained in several places.", null, 12,
            "token-exact match groups (connected overlapping matches)"),
        new(RuleIds.ParameterCount, "readability",
            "Number of declared parameters of a method, excluding a TypeScript this parameter.",
            "Keep method signatures easy to call and understand.", 5, 5, "declared parameters"),
    ];

    private static IEnumerable<RuleDefinition> ComplexityRules() =>
    [
        new(RuleIds.Cyclomatic, "complexity",
            "Cyclomatic complexity is one plus the counted decisions in a method.",
            "Identify methods with control flow that may be difficult to understand and test.", 6, 12,
            "1 base + counted decisions"),
        new(RuleIds.Nesting, "complexity",
            "Maximum depth of nested control-flow bodies (if, loops, switch, catch, conditional branches) in a method.",
            "Identify deeply nested control flow.", 2, 11, "maximum nesting depth"),
        new(RuleIds.MethodLength, "complexity",
            "Physical lines containing code (comments and blank lines excluded) from the method declaration through its body.",
            "Keep methods short enough to read in one pass.", 40, 11, "code lines"),
    ];

    private static IEnumerable<RuleDefinition> CouplingRules() =>
    [
        new(RuleIds.ModuleCycle, "coupling",
            "A strongly connected group of repository modules (projects or packages) that contains a newly added dependency edge.",
            "Prevent circular module dependencies.", 0, 15, "new cycle groups"),
        new(RuleIds.FileCycle, "coupling",
            "A strongly connected group of TypeScript files inside a package, via non-type-only imports, that contains a newly added import edge.",
            "Prevent circular file imports.", 0, 15, "new file cycle groups"),
        new(RuleIds.Boundary, "coupling",
            "A newly introduced module dependency prohibited by .quality-architecture.json.",
            "Enforce explicit architectural boundaries.", 0, 15, "new forbidden edges"),
        new(RuleIds.MainSequence, "coupling",
            "Distance D = |A + I - 1| from the main sequence, where A is abstractness and I is instability.",
            "Flag modules that are both concrete and stable, or abstract and unstable.", 0.5, 12,
            "D > 0.5 and (new module or D increased by at least 0.1)"),
    ];

    private static IEnumerable<RuleDefinition> CorrectnessRules() =>
        CorrectnessCatalog.Pinned.All.Select(rule => new RuleDefinition(
            rule.EvaluatorId, "correctness",
            $"{rule.Detects} ({rule.AnalyzerId}).",
            "Detect defects found by a pinned deterministic analyzer rule.", null, CorrectnessDeduction,
            "analyzer diagnostics not matched to the baseline"));
}


