using System.Diagnostics;

namespace CodeShield.IntegrationTests;

/// <summary>The command surface of the host: help, version and unknown commands never start the server.</summary>
public sealed class CommandLineTests
{
    private static async Task<(int Exit, string Out, string Err)> RunAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(ServerLocator.ServerDll());
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output, await error);
    }

    [Fact]
    public async Task VersionPrintsTheProductVersion()
    {
        var (exit, output, _) = await RunAsync("--version");

        Assert.Equal(0, exit);
        Assert.Matches(@"^\d+\.\d+\.\d+", output.Trim());
    }

    [Fact]
    public async Task HelpListsTheCommands()
    {
        var (exit, output, _) = await RunAsync("--help");

        Assert.Equal(0, exit);
        Assert.Contains("codeshield [mcp]", output, StringComparison.Ordinal);
        Assert.Contains("codeshield doctor", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownCommandFailsWithUsageOnStandardError()
    {
        var (exit, output, error) = await RunAsync("bogus");

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output);
        Assert.Contains("Unknown command 'bogus'", error, StringComparison.Ordinal);
    }
}
