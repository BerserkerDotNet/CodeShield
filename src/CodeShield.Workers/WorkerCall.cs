using System.Text.Json;
using CodeShield.Abstractions;

namespace CodeShield.Workers;

/// <summary>
/// Sends one request to a worker and maps the outcome to a result or a coverage reason: a runtime that cannot start is
/// <c>MISSING_ANALYZER</c>; a non-zero exit or malformed output is <c>PROJECT_LOAD_FAILED</c> unless the command classifies it.
/// </summary>
public static class WorkerCall
{
    public static async Task<(T? Result, Reason? Failure)> InvokeAsync<T>(WorkerCommand command, object body, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var run = await WorkerProcess.RunAsync(command, JsonSerializer.Serialize(body, WorkerJson.Options), cancellationToken)
                .ConfigureAwait(false);
            return run.ExitCode == 0 ? Parse<T>(command, run) : (null, command.ClassifyFailure?.Invoke(run) ?? Crashed(command, run));
        }
        catch (Exception exception) when (WorkerProcess.IsStartFailure(exception))
        {
            return (null, Missing($"{command.RuntimeName} could not be started ('{command.Executable}'): {exception.Message}"));
        }
    }

    public static Reason Missing(string message) => new(ReasonCodes.MissingAnalyzer, message);

    private static (T? Result, Reason? Failure) Parse<T>(WorkerCommand command, WorkerRun run)
        where T : class
    {
        try
        {
            var result = JsonSerializer.Deserialize<T>(run.Output, WorkerJson.Options);
            return result is null ? (null, Crashed(command, run)) : (result, null);
        }
        catch (JsonException exception)
        {
            return (null, new Reason(ReasonCodes.ProjectLoadFailed, $"The {command.WorkerName} returned invalid JSON: {exception.Message}"));
        }
    }

    private static Reason Crashed(WorkerCommand command, WorkerRun run) =>
        new(ReasonCodes.ProjectLoadFailed, $"The {command.WorkerName} failed (exit {run.ExitCode}): {run.Error.Trim()}");
}
