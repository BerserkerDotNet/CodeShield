using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CodeShield.TypeScript;

/// <summary>Where Node.js and the compiled worker live.</summary>
public sealed record WorkerLocation(string NodeExecutable, string? WorkerScript)
{
    public const string NodeVariable = "CODESHIELD_NODE";
    public const string WorkerVariable = "CODESHIELD_TS_WORKER";

    /// <summary>Resolves from environment overrides, then <c>worker/dist/main.js</c> next to the binaries or any ancestor.</summary>
    public static WorkerLocation Discover()
    {
        var node = Environment.GetEnvironmentVariable(NodeVariable);
        var script = Environment.GetEnvironmentVariable(WorkerVariable) ?? FindScript(AppContext.BaseDirectory);
        return new WorkerLocation(string.IsNullOrWhiteSpace(node) ? "node" : node, script);
    }

    private static string? FindScript(string start)
    {
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "worker", "dist", "main.js");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}

public sealed record WorkerRun(int ExitCode, string Output, string Error);

/// <summary>Starts the worker with an argument vector and exchanges one JSON document over stdin/stdout.</summary>
public static class WorkerProcess
{
    public static async Task<WorkerRun> RunAsync(WorkerLocation location, string requestJson, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(location.NodeExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(location.WorkerScript!);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Node.js could not be started.");
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
