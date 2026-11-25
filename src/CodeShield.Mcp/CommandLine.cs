using System.Reflection;

namespace CodeShield.Mcp;

/// <summary>Dispatches the command line. Human output never goes to stdout in server mode.</summary>
internal static class CommandLine
{
    public const string Usage = """
        CodeShield - deterministic code-quality evaluator for AI coding agents.

        Usage:
          codeshield [mcp]     Start the MCP server on stdio (default).
          codeshield doctor    Check the installation and prerequisites.
          codeshield --version Print the version.
          codeshield --help    Show this help.
        """;

    public static string Version =>
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        switch (args)
        {
            case []:
            case ["mcp"]:
                await McpServer.RunAsync(cancellationToken).ConfigureAwait(false);
                return 0;
            case ["--version"] or ["-v"]:
                await output.WriteLineAsync(Version).ConfigureAwait(false);
                return 0;
            case ["--help"] or ["-h"] or ["help"]:
                await output.WriteLineAsync(Usage).ConfigureAwait(false);
                return 0;
            case ["doctor"]:
                return await Doctor.RunAsync(output, cancellationToken).ConfigureAwait(false);
            default:
                await error.WriteLineAsync($"Unknown command '{string.Join(' ', args)}'.\n{Usage}").ConfigureAwait(false);
                return 2;
        }
    }
}
