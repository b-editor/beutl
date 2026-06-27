using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
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
    .AddSingleton<EncoderRegistration>()
    .AddSingleton<VideoExporter>()
    .AddSingleton<FileSessionSource>()
    .AddSingleton<AgentSessionManager>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly(typeof(AgentToolkitAssembly).Assembly)
    .WithToolsFromAssembly(typeof(Program).Assembly);

await builder.Build().RunAsync();
