using CodeShield.Abstractions;
using CodeShield.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddCodeShield(ServiceRegistration.DefaultAnalyzers());
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<QualityTools>(McpJson.Options);
await builder.Build().RunAsync();

