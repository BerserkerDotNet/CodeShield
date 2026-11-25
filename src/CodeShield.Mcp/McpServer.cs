using CodeShield.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodeShield.Mcp;

internal static class McpServer
{
    public static async Task RunAsync(CancellationToken cancellationToken)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddCodeShield(ServiceRegistration.DefaultAnalyzers());
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<QualityTools>(McpJson.Options);
        await builder.Build().RunAsync(cancellationToken).ConfigureAwait(false);
    }
}
