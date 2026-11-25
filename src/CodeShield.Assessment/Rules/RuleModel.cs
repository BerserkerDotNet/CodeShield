using CodeShield.Abstractions;
using CodeShield.Core.Contracts;
using CodeShield.Core.Configuration;
using CodeShield.Core.Rulesets;
using CodeShield.Core.Scope;

namespace CodeShield.Core.Rules;

using EvidenceItem = IReadOnlyDictionary<string, object?>;

/// <summary>A scored or context-only finding with its full calculation and evidence.</summary>
public sealed record RuleFinding(
    RuleDefinition Rule,
    string ContextId,
    string Attribution,
    int Deduction,
    IReadOnlyList<FindingLocation> Locations,
    object? Actual,
    object? Limit,
    string Calculation,
    IReadOnlyList<EvidenceItem> Evidence);

public sealed class RuleOutput
{
    public List<RuleFinding> Findings { get; } = [];

    public List<CoverageEntry> Coverage { get; } = [];
}

/// <summary>Facts of one analyzed snapshot, merged across languages.</summary>
public sealed class SnapshotFacts(IReadOnlyList<ContextFacts> contexts, ModuleGraphFacts graph)
{
    public static SnapshotFacts Empty { get; } = new([], ModuleGraphFacts.Empty);

    public IReadOnlyList<ContextFacts> Contexts { get; } = contexts;

    public ModuleGraphFacts Graph { get; } = graph;

    public ContextFacts? FindContext(string contextId) =>
        Contexts.FirstOrDefault(context => context.ContextId == contextId);

    public FileFact? FindFile(string path) =>
        Contexts.SelectMany(context => context.Files).FirstOrDefault(file => file.Path == path);
}

public sealed record ArchitectureRule(string From, string To);

/// <summary>The optional boundary configuration; <c>Error</c> is set when it exists but is invalid.</summary>
public sealed record ArchitectureConfig(IReadOnlyList<ArchitectureRule> Forbidden, string? Error);

public sealed record RuleInput(
    Ruleset Ruleset,
    ChangeScope Scope,
    SnapshotFacts Current,
    SnapshotFacts Baseline,
    IReadOnlyDictionary<string, IReadOnlyList<FileTokens>> Tokens,
    ArchitectureConfig? Architecture,
    IReadOnlyList<Reason> GraphFailures)
{
    public ChangeMap Map => Scope.Map;

    /// <summary>The cascading limits and exclusions; defaults when no configuration exists.</summary>
    public RulePolicy Policy { get; init; } = RulePolicy.Defaults;
}

public interface IRule
{
    IReadOnlyList<string> Ids { get; }

    RuleOutput Evaluate(RuleInput input);
}
