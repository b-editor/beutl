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

// The one MCP server agents install: headless editing of project files, plus live forwarding to
// the running Beutl editors of the profile whenever a call names an instanceId.
public static class AgentToolkitMcpServer
{
    public static IServiceCollection AddAgentToolkitServer(
        this IServiceCollection services, string profileDirectory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);

        // Without the decoders the app registers at startup, every video, sound and animated image
        // would render as offline media and analyze_audio_rhythm could not open any file.
        DecoderRegistration.EnsureRegistered();

        services
            .AddSingleton<IOutputOperationLeaseProvider>(StandaloneOutputOperationLeaseProvider.Instance)
            .AddAgentToolkitRendering()
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
            .WithSharedAgentToolkitTools();
    }

    // The services and tools both hosts share; each host adds its own lease provider, sessions and tools.
    public static IServiceCollection AddAgentToolkitRendering(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services
            .AddSingleton<DestructiveGuard>()
            .AddSingleton<StillRenderer>()
            .AddSingleton<StoryboardRenderer>()
            .AddSingleton<FrameDifferenceAnalyzer>()
            .AddSingleton<AudioRhythmAnalyzer>()
            .AddSingleton<EncoderRegistration>()
            .AddSingleton<VideoExporter>()
            .AddSingleton<RenderJobManager>();
    }

    public static IMcpServerBuilder WithSharedAgentToolkitTools(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .WithTools<SessionTools>()
            .WithTools<QueryTools>()
            .WithTools<EditTools>()
            .WithTools<HistoryTools>()
            .WithTools<RenderTools>();
    }
}
