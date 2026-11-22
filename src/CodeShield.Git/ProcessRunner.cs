using System.Diagnostics;

namespace CodeShield.Git;

public sealed record ProcessResult(int ExitCode, byte[] Output, string Error);

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken);
}

/// <summary>Starts a process with an argument vector (never a shell string) and captures its output.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        process.StandardInput.Close();
        var output = ReadAllAsync(process.StandardOutput.BaseStream, cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
