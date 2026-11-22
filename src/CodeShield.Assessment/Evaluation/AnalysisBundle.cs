using CodeShield.Abstractions;
using CodeShield.Core.Rules;

namespace CodeShield.Core.Evaluation;

/// <summary>The merged result of analyzing one snapshot with every language adapter that had work.</summary>
public sealed record SnapshotAnalysis(
    SnapshotFacts Facts,
    IReadOnlyList<LanguageAnalysis> Languages,
    IReadOnlyList<InputRecord> Inputs)
{
    public static SnapshotAnalysis Empty { get; } = new(SnapshotFacts.Empty, [], []);

    public IEnumerable<KeyValuePair<string, string>> Versions =>
        Languages.SelectMany(language => language.AnalyzerVersions);
}

/// <summary>Everything the rules need, gathered once per evaluation.</summary>
public sealed record AnalysisBundle(
    SnapshotAnalysis Current,
    SnapshotAnalysis Baseline,
    IReadOnlyDictionary<string, IReadOnlyList<FileTokens>> Tokens,
    IReadOnlyList<Reason> TokenFailures,
    ArchitectureConfig? Architecture,
    IReadOnlyList<InputRecord> Inputs,
    bool InputsDrifted = false,
    IReadOnlyList<Reason>? UnreadableInputs = null);
