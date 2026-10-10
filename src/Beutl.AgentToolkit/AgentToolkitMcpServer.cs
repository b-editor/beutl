using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Live;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Beutl.AgentToolkit;

// The one MCP server agents install: headless project editing under the workspace, plus live
// forwarding to the running Beutl editors of the profile whenever a call names an instanceId.
public static class AgentToolkitMcpServer
{
    public static IServiceCollection AddAgentToolkitServer(
        this IServiceCollection services, string workspaceRoot, string profileDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);

        services
            .AddSingleton<IWorkspaceGuard>(_ => new WorkspaceGuard(workspaceRoot))
            .AddSingleton<IOutputOperationLeaseProvider>(StandaloneOutputOperationLeaseProvider.Instance)
            .AddSingleton<DestructiveGuard>()
            .AddSingleton<StillRenderer>()
            .AddSingleton<StoryboardRenderer>()
            .AddSingleton<FrameDifferenceAnalyzer>()
            .AddSingleton<AudioRhythmAnalyzer>()
            .AddSingleton<EncoderRegistration>()
            .AddSingleton<VideoExporter>()
            .AddSingleton<RenderJobManager>()
            .AddSingleton<FileSessionSource>()
            .AddSingleton<IProjectSessionGateway, FileProjectSessionGateway>()
            .AddSingleton<AgentSessionManager>();

        services.AddSingleton(provider => LiveMcpBroker.CreateForProfile(
            profileDirectory, provider.GetService<ILoggerFactory>()?.CreateLogger<LiveMcpBroker>()));
        services.AddOptions<McpServerOptions>()
            .Configure<LiveMcpBroker>((options, broker) => broker.ConfigureServer(options));
        return services;
    }

    public static IMcpServerBuilder WithAgentToolkitTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .WithRequestFilters(filters =>
            {
                LiveMcpBroker.AddFilters(filters);
                filters.AddToolkitCallToolErrorFilter();
            })
            .WithTools<LiveInstanceTools>()
            .WithTools<SessionTools>()
            .WithTools<QueryTools>()
            .WithTools<EditTools>()
            .WithTools<HistoryTools>()
            .WithTools<RenderTools>();
    }
}
