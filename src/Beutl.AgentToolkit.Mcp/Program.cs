using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using var beutlLoggerFactory = LoggerFactory.Create(ConfigureConsoleLogging);
Log.LoggerFactory = beutlLoggerFactory;

var builder = Host.CreateApplicationBuilder(args);
string workspaceRoot = Environment.GetEnvironmentVariable("BEUTL_WORKSPACE")
                       ?? Directory.GetCurrentDirectory();

ConfigureConsoleLogging(builder.Logging);

builder.Services
    .AddSingleton<IWorkspaceGuard>(_ => new WorkspaceGuard(workspaceRoot))
    .AddSingleton<DestructiveGuard>()
    .AddSingleton<StillRenderer>()
    .AddSingleton<StoryboardRenderer>()
    .AddSingleton<MotionVariationAnalyzer>()
    .AddSingleton<AudioRhythmAnalyzer>()
    .AddSingleton<QualityAnalyzer>()
    .AddSingleton<EncoderRegistration>()
    .AddSingleton<VideoExporter>()
    .AddSingleton<RenderJobManager>()
    .AddSingleton<FileSessionSource>()
    .AddSingleton<IProjectSessionGateway, FileProjectSessionGateway>()
    .AddSingleton(_ => new CreativeMemoryStore(workspaceRoot))
    .AddSingleton<AgentSessionManager>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithRequestFilters(filters => filters.AddToolkitCallToolErrorFilter())
    .WithTools<SessionTools>()
    .WithTools<QueryTools>()
    .WithTools<DesignTools>()
    .WithTools<EditTools>()
    .WithTools<RenderTools>();

await builder.Build().RunAsync();

static void ConfigureConsoleLogging(ILoggingBuilder logging)
{
    logging.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
}
