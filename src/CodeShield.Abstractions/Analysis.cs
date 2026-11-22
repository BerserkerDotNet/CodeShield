namespace CodeShield.Abstractions;

public sealed record ModuleEdge(string From, string To, bool TypeOnly);

public sealed record FileImport(string From, string To, bool TypeOnly);

/// <summary>A module edge that coexists with a context's own edges but is owned (and charged) by another context.</summary>
public sealed record SupportEdge(ModuleEdge Edge, string ContextId);

/// <summary>
/// The dependency facts of one analysis context: a framework slice for C# (every project resolved for one target framework)
/// or one tsconfig for TypeScript. <c>Edges</c> are owned by the context; <c>Support</c> edges belong to contexts that can
/// coexist with it. Edges of mutually exclusive alternatives never share a context.
/// </summary>
public sealed record ContextGraph(string ContextId, IReadOnlyList<ModuleEdge> Edges, IReadOnlyList<FileImport> FileImports)
{
    public IReadOnlyList<SupportEdge> Support { get; init; } = [];
}

/// <summary>
/// Repository module graph. Modules are .csproj or package.json paths. <c>Edges</c> and <c>FileImports</c> are the
/// union over contexts; <c>Contexts</c> keeps each context's own facts so rules never invent cycles from edges that never coexist.
/// </summary>
public sealed record ModuleGraphFacts(
    IReadOnlyList<string> Modules,
    IReadOnlyList<ModuleEdge> Edges,
    IReadOnlyList<FileImport> FileImports,
    IReadOnlyList<Reason> Unresolved)
{
    public static ModuleGraphFacts Empty { get; } = new([], [], [], []);

    public IReadOnlyList<ContextGraph> Contexts { get; init; } = [];
}

public static class InputScopes
{
    public const string Repository = "repository";
    public const string External = "external";
}

/// <summary>A file read during analysis; the hash is taken from the bytes that were analyzed.</summary>
public sealed record InputRecord(string Scope, string Path, string Sha256);

public sealed record AnalysisRequest(
    string SnapshotRoot,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> FocusFiles,
    IReadOnlyList<string> ChangedConfigFiles,
    bool IsBaseline);

/// <summary>Result of analyzing one snapshot for one language.</summary>
public sealed record LanguageAnalysis(
    string Language,
    IReadOnlyDictionary<string, string> AnalyzerVersions,
    IReadOnlyList<ContextFacts> Contexts,
    ModuleGraphFacts Graph,
    IReadOnlyList<InputRecord> Inputs,
    IReadOnlyList<string> UnownedFiles,
    IReadOnlyList<Reason> Failures);

public sealed record Token(string Kind, string Text, int Line, int Column, int EndLine, int EndColumn, int Offset);

public sealed record FileTokens(string Path, IReadOnlyList<Token> Tokens);

public sealed record TokenizeRequest(string SnapshotRoot, IReadOnlyList<string> Files);

public sealed record TokenizeResult(IReadOnlyList<FileTokens> Files, IReadOnlyList<Reason> Failures);
