using System.ComponentModel;
using System.Diagnostics;

namespace CodeShield.Mcp;

/// <summary>
/// Runs an external tool with one argument and returns its trimmed standard output (or standard error, for tools such as
/// <c>java -version</c> that report there), or null when it is missing or fails.
/// </summary>
internal static class ToolProbe
{
    public static async Task<string?> RunAsync(string executable, string argument, CancellationToken cancellationToken, bool readError = false)
    {
        var start = new ProcessStartInfo(executable, argument)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(output, error).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var text = await (readError ? error : output).ConfigureAwait(false);
            return process.ExitCode == 0 ? text.Trim() : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
