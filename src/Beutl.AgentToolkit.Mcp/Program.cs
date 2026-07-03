using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
string workspaceRoot = Environment.GetEnvironmentVariable("BEUTL_WORKSPACE")
                       ?? Directory.GetCurrentDirectory();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

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
    .AddSingleton(_ => new CreativeMemoryStore(workspaceRoot))
    .AddSingleton<AgentSessionManager>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SessionTools>()
    .WithTools<QueryTools>()
    .WithTools<DesignTools>()
    .WithTools<EditTools>()
    .WithTools<RenderTools>();

await builder.Build().RunAsync();
