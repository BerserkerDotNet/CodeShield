namespace CodeShield.Mcp;

internal static class Doctor
{
    public static async Task<int> RunAsync(TextWriter output, CancellationToken cancellationToken)
    {
        await output.WriteLineAsync($"CodeShield {CommandLine.Version}").ConfigureAwait(false);
        var checks = await InstallationChecks.RunAsync(cancellationToken).ConfigureAwait(false);
        foreach (var check in checks)
        {
            await output.WriteLineAsync($"[{(check.Passed ? "ok" : "FAIL")}] {check.Name}: {check.Detail}").ConfigureAwait(false);
        }

        var failed = checks.Count(check => !check.Passed);
        await output.WriteLineAsync(failed == 0 ? "All checks passed." : $"{failed} check(s) failed.").ConfigureAwait(false);
        return failed == 0 ? 0 : 1;
    }
}
