namespace CodeShield.Abstractions;

/// <summary>Which static-correctness rules are selected, shared by the adapters that run them and the assessor that scores them.</summary>
public interface ICorrectnessSelection
{
    IReadOnlyList<CorrectnessRule> All { get; }

    IEnumerable<CorrectnessRule> ForLanguage(string language);

    CorrectnessRule? FindByAnalyzerId(string language, string analyzerId);
}

/// <summary>Selection backed by a fixed rule list.</summary>
public sealed class CorrectnessSelection(IReadOnlyList<CorrectnessRule> rules) : ICorrectnessSelection
{
    public IReadOnlyList<CorrectnessRule> All => rules;

    public IEnumerable<CorrectnessRule> ForLanguage(string language) =>
        rules.Where(rule => rule.Language == language);

    public CorrectnessRule? FindByAnalyzerId(string language, string analyzerId) =>
        ForLanguage(language).FirstOrDefault(rule => string.Equals(rule.AnalyzerId, analyzerId, StringComparison.OrdinalIgnoreCase));
}

