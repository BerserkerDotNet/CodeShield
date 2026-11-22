using System.Text.Json;
using CodeShield.Abstractions;

namespace CodeShield.TypeScript;

/// <summary>TypeScript adapter: delegates analysis and tokenization to the bundled Node.js worker.</summary>
public sealed class TypeScriptAnalyzer(WorkerLocation location, ICorrectnessSelection correctness) : ILanguageAnalyzer
{
    private const int Protocol = 1;

    private static readonly string[] SourceExtensions = [".ts", ".tsx", ".mts", ".cts"];

    public TypeScriptAnalyzer()
        : this(WorkerLocation.Discover(), CorrectnessCatalog.Pinned)
    {
    }

    public string Language => Languages.TypeScript;

    public bool IsSource(string repoRelativePath) =>
        !InNodeModules(repoRelativePath)
        && SourceExtensions.Any(extension => repoRelativePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        && !repoRelativePath.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase);

    public bool IsConfiguration(string repoRelativePath)
    {
        var name = repoRelativePath[(repoRelativePath.LastIndexOf('/') + 1)..];
        var isConfig = name == "package.json"
            || (name.StartsWith("tsconfig", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
        return isConfig && !InNodeModules(repoRelativePath);
    }

    public async Task<LanguageAnalysis> AnalyzeAsync(AnalysisRequest request, CancellationToken cancellationToken)
    {
        var body = new
        {
            protocol = Protocol,
            command = "analyze",
            root = request.SnapshotRoot,
            files = request.Files,
            focusFiles = request.FocusFiles,
            changedConfigFiles = request.ChangedConfigFiles,
            isBaseline = request.IsBaseline,
            correctnessRules = correctness.ForLanguage(Language)
                .Select(rule => new { analyzerId = rule.AnalyzerId, evaluatorId = rule.EvaluatorId, options = JsonDocument.Parse(rule.OptionsJson).RootElement.Clone() }),
        };
        var (result, failure) = await CallAsync<LanguageAnalysis>(body, cancellationToken).ConfigureAwait(false);
        return result ?? new LanguageAnalysis(Language, new Dictionary<string, string>(), [], ModuleGraphFacts.Empty, [], [], [failure!]);
    }

    public async Task<TokenizeResult> TokenizeAsync(TokenizeRequest request, CancellationToken cancellationToken)
    {
        var body = new { protocol = Protocol, command = "tokenize", root = request.SnapshotRoot, files = request.Files };
        var (result, failure) = await CallAsync<TokenizeResult>(body, cancellationToken).ConfigureAwait(false);
        return result ?? new TokenizeResult([], [failure!]);
    }

    private static bool InNodeModules(string path) =>
        path.StartsWith("node_modules/", StringComparison.Ordinal) || path.Contains("/node_modules/", StringComparison.Ordinal);

    private async Task<(T? Result, Reason? Failure)> CallAsync<T>(object body, CancellationToken cancellationToken)
        where T : class
    {
        if (location.WorkerScript is null || !File.Exists(location.WorkerScript))
        {
            return (null, Missing("The TypeScript worker (worker/dist/main.js) was not found; run scripts/setup."));
        }

        try
        {
            var run = await WorkerProcess.RunAsync(location, JsonSerializer.Serialize(body, WorkerJson.Options), cancellationToken)
                .ConfigureAwait(false);
            return run.ExitCode == 0 ? Parse<T>(run) : (null, Crashed(run));
        }
        catch (Exception exception) when (WorkerProcess.IsStartFailure(exception))
        {
            return (null, Missing($"Node.js could not be started ('{location.NodeExecutable}'): {exception.Message}"));
        }
    }

    private static (T? Result, Reason? Failure) Parse<T>(WorkerRun run)
        where T : class
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(run.Output, WorkerJson.Options);
            return result is null ? (null, Crashed(run)) : (result, null);
        }
        catch (JsonException exception)
        {
            return (null, new Reason(ReasonCodes.ProjectLoadFailed, $"The TypeScript worker returned invalid JSON: {exception.Message}"));
        }
    }

    private static Reason Missing(string message) => new(ReasonCodes.MissingAnalyzer, message);

    private static Reason Crashed(WorkerRun run) =>
        new(ReasonCodes.ProjectLoadFailed, $"The TypeScript worker failed (exit {run.ExitCode}): {run.Error.Trim()}");
}



