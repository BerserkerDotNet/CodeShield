using CodeShield.Abstractions;

namespace CodeShield.CSharp;

/// <summary>Evaluator rule IDs that this adapter measures or can fail to measure.</summary>
internal static class RuleIds
{
    public const string Cyclomatic = "complexity.cyclomatic";
    public const string Nesting = "complexity.nesting";
    public const string MethodLength = "complexity.method-length";
    public const string ParameterCount = "readability.parameter-count";
    public const string Lcom4 = "cohesion.lcom4";

    public static IReadOnlyList<string> Correctness { get; } =
        [.. CorrectnessCatalog.Pinned.ForLanguage(Languages.CSharp).Select(rule => rule.EvaluatorId)];

    /// <summary>Rules that need a semantic model: LCOM4 and every correctness rule.</summary>
    public static IReadOnlyList<string> TypeAware { get; } = [Lcom4, .. Correctness];

    public static string ForAnalyzer(string analyzerId) =>
        CorrectnessCatalog.Pinned.FindByAnalyzerId(Languages.CSharp, analyzerId)?.EvaluatorId ?? analyzerId;

    public static IReadOnlyList<Reason> Fail(string code, string message, IEnumerable<string> ruleIds, string contextId) =>
        [.. ruleIds.Select(rule => new Reason(code, message, rule, contextId))];
}


