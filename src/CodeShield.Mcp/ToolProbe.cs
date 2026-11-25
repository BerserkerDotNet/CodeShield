using System.ComponentModel;
using System.Diagnostics;

namespace CodeShield.Mcp;

/// <summary>Runs an external tool with one argument and returns its trimmed standard output, or null when it is missing or fails.</summary>
internal static class ToolProbe
{
    public static async Task<string?> RunAsync(string executable, string argument, CancellationToken cancellationToken)
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

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}
