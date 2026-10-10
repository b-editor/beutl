using Beutl;
using Beutl.AgentToolkit;
using Beutl.AgentToolkit.Live;
using Beutl.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// One MCP server for both ways of working: a call that carries an instanceId is forwarded to that
// running Beutl editor; a call without one edits project files headlessly.
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
ConfigureConsoleLogging(builder.Logging);

builder.Services.AddAgentToolkitServer(BeutlEnvironment.GetHomeDirectoryPath());
builder.Services.AddHostedService<LiveInstanceWatcher>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithAgentToolkitTools();

var host = builder.Build();
Log.LoggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
await host.RunAsync();

static void ConfigureConsoleLogging(ILoggingBuilder logging)
{
    logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
}

// Emits tools/list_changed while Beutl editors start and exit, so an agent that connected before
// an editor was open picks up the live-only tools without reconnecting.
sealed class LiveInstanceWatcher(LiveMcpBroker broker) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => broker.WatchAsync(stoppingToken);
}
