namespace CodeShield.Abstractions;

public sealed record DecisionEvidence(string DecisionKind, int Line);

public sealed record NestingEvidence(string ConstructKind, int Line, int Depth);

/// <summary>Measurements of one executable body. Nested functions are separate facts.</summary>
public sealed record MethodFact(
    string UnitId,
    string Name,
    SourceSpan Span,
    int Complexity,
    int Nesting,
    int CodeLines,
    int ParameterCount,
    IReadOnlyList<DecisionEvidence> Decisions,
    IReadOnlyList<NestingEvidence> NestingPoints);

/// <summary>A focus file owned by a context. Methods are populated for focus files only.</summary>
public sealed record FileFact(string Path, int CodeLines, bool Generated, IReadOnlyList<MethodFact> Methods);

public sealed record CohesionNode(string Id, string Name);

public sealed record CohesionEdge(string From, string To, string Reason);

public sealed record ClassLocation(string File, SourceSpan Span);

public sealed record ClassFact(
    string UnitId,
    string Name,
    IReadOnlyList<ClassLocation> Locations,
    bool Applicable,
    IReadOnlyList<CohesionNode> Nodes,
    IReadOnlyList<CohesionEdge> Edges,
    IReadOnlyList<IReadOnlyList<string>> Components);

public sealed record TypeFact(string UnitId, string File, bool IsAbstract);

/// <summary>An analyzer diagnostic. <c>AnalyzerId</c> is the underlying ID, e.g. CA2000.</summary>
public sealed record DiagnosticFact(string AnalyzerId, string File, SourceSpan Span, string Message, string UnitId);

/// <summary>
/// Facts for one analysis context (project plus framework, or tsconfig).
/// <c>Failures</c> carry evaluator rule IDs of checks that could not run.
/// </summary>
public sealed record ContextFacts(
    string ContextId,
    string Language,
    string ConfigPath,
    string? Framework,
    string? ModulePath,
    IReadOnlyList<string> OwnedFiles,
    IReadOnlyList<FileFact> Files,
    IReadOnlyList<ClassFact> Classes,
    IReadOnlyList<TypeFact> Types,
    IReadOnlyList<DiagnosticFact> Diagnostics,
    IReadOnlyList<Reason> Failures);
