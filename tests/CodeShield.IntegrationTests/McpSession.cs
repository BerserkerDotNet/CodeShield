using System.Text.Json;
using ModelContextProtocol.Client;

namespace CodeShield.IntegrationTests;

/// <summary>A real CodeShield MCP server process driven over stdio.</summary>
public sealed class McpSession : IAsyncDisposable
{
    /// <summary>
    /// The SDK's 60-second initialize deadline is sized for interactive clients. Each test starts its own server while other
    /// tests saturate the runner with Roslyn, Node and JVM workers, and a shared CI runner has exceeded it on a server that
    /// normally answers in a few seconds; a hung server still fails the test.
    /// </summary>
    private static readonly TimeSpan InitializeDeadline = TimeSpan.FromMinutes(3);

    private readonly McpClient client;

    private McpSession(McpClient client) => this.client = client;

    public static Task<McpSession> StartAsync(IReadOnlyDictionary<string, string>? environment = null) =>
        ConnectAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "codeshield",
            Command = "dotnet",
            Arguments = [ServerLocator.ServerDll()],
            EnvironmentVariables = environment?.ToDictionary(pair => pair.Key, pair => (string?)pair.Value),
        }));

    /// <summary>Starts an installed executable (for example the global tool) from an unrelated working directory.</summary>
    public static Task<McpSession> StartInstalledAsync(string executable, string workingDirectory, params string[] arguments) =>
        ConnectAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "codeshield",
            Command = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
        }));

    private static async Task<McpSession> ConnectAsync(StdioClientTransport transport) =>
        new(await McpClient.CreateAsync(transport, new McpClientOptions { InitializationTimeout = InitializeDeadline }));

    public Task<JsonElement> EvaluateAsync(string workspace, string baseline, string contractVersion = "1") =>
        CallAsync("evaluate_quality", new() { ["contractVersion"] = contractVersion, ["workspace"] = workspace, ["baseline"] = baseline });

    public Task<JsonElement> EvaluateRulesetAsync(string workspace, string baseline, string ruleset) =>
        CallAsync("evaluate_quality", new() { ["contractVersion"] = "1", ["workspace"] = workspace, ["baseline"] = baseline, ["ruleset"] = ruleset });

    public Task<JsonElement> DetailsAsync(string evaluationId, string ruleId, string contractVersion = "1") =>
        CallAsync("get_rule_details", new() { ["contractVersion"] = contractVersion, ["evaluationId"] = evaluationId, ["ruleId"] = ruleId });

    public async Task<IReadOnlyList<string>> ToolNamesAsync() =>
        (await client.ListToolsAsync()).Select(tool => tool.Name).Order(StringComparer.Ordinal).ToList();

    private async Task<JsonElement> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(tool, arguments);
        var node = result.StructuredContent ?? throw new InvalidOperationException("Tool returned no structured content.");
        return node.Deserialize<JsonElement>();
    }

    public async ValueTask DisposeAsync() => await client.DisposeAsync();
}

internal static class ServerLocator
{
    public static string ServerDll()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var bin = Path.Combine(directory.FullName, "src", "CodeShield.Mcp", "bin");
            var found = Directory.Exists(bin) ? Directory.GetFiles(bin, "codeshield.dll", SearchOption.AllDirectories).FirstOrDefault() : null;
            if (found is not null)
            {
                return found;
            }
        }

        throw new FileNotFoundException("Build src/CodeShield.Mcp first.");
    }
}
