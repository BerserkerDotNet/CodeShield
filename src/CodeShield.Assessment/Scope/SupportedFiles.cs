using CodeShield.Abstractions;

namespace CodeShield.Core.Scope;

/// <summary>Classifies repository paths using the registered language adapters.</summary>
public sealed class SupportedFiles(IEnumerable<ILanguageAnalyzer> analyzers)
{
    public const string ArchitectureConfig = ".quality-architecture.json";

    private readonly IReadOnlyList<ILanguageAnalyzer> _analyzers = [.. analyzers];

    public IReadOnlyList<ILanguageAnalyzer> Analyzers => _analyzers;

    public bool IsSource(string path) => _analyzers.Any(analyzer => analyzer.IsSource(path));

    public bool IsConfiguration(string path) =>
        path == ArchitectureConfig || _analyzers.Any(analyzer => analyzer.IsConfiguration(path));

    public bool IsRelevant(string path) => IsSource(path) || IsConfiguration(path);

    public string? LanguageOf(string path) =>
        _analyzers.FirstOrDefault(analyzer => analyzer.IsSource(path))?.Language;
}
