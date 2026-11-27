using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CodeShield.Abstractions;

namespace CodeShield.Workers;

/// <summary>
/// How to start one language worker: the runtime executable and its argument vector.
/// <paramref name="WorkerName"/> and <paramref name="RuntimeName"/> are used in failure messages.
/// </summary>
public sealed record WorkerCommand(string WorkerName, string RuntimeName, string Executable, IReadOnlyList<string> Arguments)
{
    /// <summary>Optionally maps a non-zero exit to a more specific reason than <c>PROJECT_LOAD_FAILED</c>.</summary>
    public Func<WorkerRun, Reason?>? ClassifyFailure { get; init; }
}

public sealed record WorkerRun(int ExitCode, string Output, string Error);

/// <summary>Starts a worker with an argument vector and exchanges one JSON document over stdin/stdout.</summary>
public static class WorkerProcess
{
    public static async Task<WorkerRun> RunAsync(WorkerCommand command, string requestJson, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(command.Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in command.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{command.RuntimeName} could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardInput.WriteAsync(requestJson.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new WorkerRun(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    public static bool IsStartFailure(Exception exception) =>
        exception is Win32Exception or InvalidOperationException or IOException;
}
