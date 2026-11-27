using System.Text.Json;
using CodeShield.Abstractions;

namespace CodeShield.Workers;

/// <summary>
/// A language adapter whose analysis and tokenization run in an out-of-process worker speaking the shared JSON protocol
/// (version 1). Subclasses classify repository files and supply the command that starts their worker; requests, the selected
/// correctness rules and the failure results when the worker cannot run are built here.
/// </summary>
public abstract class WorkerLanguageAnalyzer(ICorrectnessSelection correctness) : ILanguageAnalyzer
{
    protected const int Protocol = 1;

    public abstract string Language { get; }

    public abstract bool IsSource(string repoRelativePath);

    public abstract bool IsConfiguration(string repoRelativePath);

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

    /// <summary>The command that starts this adapter's worker, or the reason it cannot start (usually <c>MISSING_ANALYZER</c>).</summary>
    protected abstract (WorkerCommand? Command, Reason? Failure) ResolveCommand();

    /// <summary>Sends one request to the worker; when no command can be resolved, its reason is the failure and nothing is started.</summary>
    protected Task<(T? Result, Reason? Failure)> CallAsync<T>(object body, CancellationToken cancellationToken)
        where T : class
    {
        var (command, failure) = ResolveCommand();
        return command is null
            ? Task.FromResult<(T?, Reason?)>((null, failure))
            : WorkerCall.InvokeAsync<T>(command, body, cancellationToken);
    }
}
