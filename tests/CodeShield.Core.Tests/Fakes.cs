using CodeShield.Abstractions;
using CodeShield.Core.Git;

namespace CodeShield.Core.Tests;

/// <summary>In-memory Git: the unit tests never start a real git process.</summary>
public sealed class FakeGit(string topLevel) : IGitClient
{
    private readonly List<string> _calls = [];

    public List<NameStatusEntry> Changes { get; } = [];

    public List<string> Untracked { get; } = [];

    public List<string> Paths { get; } = [];

    public string Diff { get; set; } = "";

    public GitException? ResolveFailure { get; set; }

    public Task<string> GetTopLevelAsync(string workspace, CancellationToken cancellationToken) => Task.FromResult(Record(topLevel));

    public Task<string> ResolveCommitAsync(string root, string revision, CancellationToken cancellationToken) =>
        ResolveFailure is null ? Task.FromResult(Record(new string('a', 40))) : Task.FromException<string>(ResolveFailure);

    public Task<IReadOnlyList<NameStatusEntry>> GetNameStatusAsync(string root, string baseline, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NameStatusEntry>>(Record(Changes));

    public Task<string> GetUnifiedDiffAsync(string root, string baseline, IReadOnlyList<string> paths, CancellationToken cancellationToken) =>
        Task.FromResult(Record(Diff));

    public Dictionary<string, string> WorkingFileDiffs { get; } = [];

    public Task<string> GetUnifiedDiffToWorkingFileAsync(string root, string baseline, string path, CancellationToken cancellationToken) =>
        Task.FromResult(Record(WorkingFileDiffs.GetValueOrDefault(path, "")));

    public Task<IReadOnlyList<string>> ListUntrackedAsync(string root, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Record(Untracked));

    public Task<IReadOnlyList<string>> ListBaselinePathsAsync(string root, string commit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Record(Paths));

    public Task<IReadOnlyList<string>> ListCurrentPathsAsync(string root, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Record(Paths));

    public Task ExportTreeAsync(string root, string commit, string destination, CancellationToken cancellationToken) =>
        Record(Task.CompletedTask);

    /// <summary>Names of the Git operations invoked so far.</summary>
    public IReadOnlyList<string> Calls => _calls;

    private T Record<T>(T value, [System.Runtime.CompilerServices.CallerMemberName] string operation = "")
    {
        _calls.Add(operation);
        return value;
    }
}

/// <summary>A TypeScript-like analyzer returning canned facts: <c>count</c> methods with the given parameter count in <c>a.ts</c>.</summary>
public sealed class FakeAnalyzer(int methodCount, int parameterCount, Reason? failure = null) : ILanguageAnalyzer
{
    public string Language => Languages.TypeScript;

    public bool IsSource(string repoRelativePath) => repoRelativePath.EndsWith(".ts", StringComparison.Ordinal);

    public bool IsConfiguration(string repoRelativePath) => repoRelativePath.EndsWith("package.json", StringComparison.Ordinal);

    public Task<LanguageAnalysis> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        var methods = Enumerable.Range(1, methodCount)
            .Select(line => new MethodFact($"a.ts#m{line}", $"m{line}", new SourceSpan(line, 1, line, 20), 1, 0, 1, parameterCount, [], []))
            .ToList();
        var file = new FileFact("a.ts", methodCount, false, methods);
        var context = new ContextFacts("tsconfig.json", Language, "tsconfig.json", null, "package.json", ["a.ts"], [file], [], [], [], []);
        IReadOnlyList<Reason> failures = failure is null ? [] : [failure];
        return Task.FromResult(new LanguageAnalysis(Language, new Dictionary<string, string>(), [context], ModuleGraphFacts.Empty, [], [], failures));
    }

    public Task<TokenizeResult> TokenizeAsync(TokenizeRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(new TokenizeResult([], []));
}

/// <summary>A Kotlin-like analyzer whose runtime is missing: every call fails with <c>MISSING_ANALYZER</c> and is recorded in <paramref name="calls"/>.</summary>
public sealed class UnavailableAnalyzer(List<string> calls) : ILanguageAnalyzer
{
    private static readonly Reason Missing = new(ReasonCodes.MissingAnalyzer, "no detekt");

    public string Language => Languages.Kotlin;

    public bool IsSource(string repoRelativePath) => repoRelativePath.EndsWith(".kt", StringComparison.Ordinal);

    public bool IsConfiguration(string repoRelativePath) => repoRelativePath.EndsWith(".gradle.kts", StringComparison.Ordinal);

    public Task<LanguageAnalysis> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        calls.Add(nameof(AnalyzeAsync));
        return Task.FromResult(new LanguageAnalysis(Language, new Dictionary<string, string>(), [], ModuleGraphFacts.Empty, [], [], [Missing]));
    }

    public Task<TokenizeResult> TokenizeAsync(TokenizeRequest request, CancellationToken cancellationToken)
    {
        calls.Add(nameof(TokenizeAsync));
        return Task.FromResult(new TokenizeResult([], [Missing]));
    }
}
