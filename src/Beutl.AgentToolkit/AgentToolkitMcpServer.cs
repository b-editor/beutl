using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Installation;
using Beutl.AgentToolkit.Live;
using Beutl.AgentToolkit.NodeGraphs;
using Beutl.AgentToolkit.Rendering;
using Beutl.AgentToolkit.Schema;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tools;
using Beutl.AgentToolkit.Workspace;
using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Media.Decoding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Extensions.Skills;
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

        var configuration = new GlobalConfiguration();
        var decoders = new DecoderRegistry(configuration.ExtensionConfig);
        var fonts = new Beutl.Media.FontManager(configuration.FontConfig);

        // Without the decoders the app registers at startup, every video, sound and animated image
        // would render as offline media and analyze_audio_rhythm could not open any file.
        DecoderRegistration.EnsureRegistered(decoders);
        // Without the app's fallback fonts, CJK and emoji in a font that lacks them would render as boxes.
        BundledFontRegistration.EnsureRegistered(fonts);
        // The app registers its built-in nodes at startup; without them list_node_types and edit_node_graph know none.
        NodeTypeCatalog.EnsureBuiltInNodesRegistered();

        services
            .AddSingleton(configuration)
            .AddSingleton(fonts)
            .AddSingleton(decoders)
            .AddSingleton(_ => new ObjectTemplateService(
                new SceneRenderServices(fonts, decoders, configuration.EditorConfig, configuration.ProxyStoreConfig),
                Path.Combine(profileDirectory, "templates")))
            .AddSingleton(_ => TypeRegistration.CreateLibraryService())
            .AddSingleton<SchemaGenerator>()
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
            .WithSharedAgentToolkitTools()
            .WithAgentToolkitSkills();
    }

    public static IMcpServerBuilder WithAgentToolkitSkills(this IMcpServerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

#pragma warning disable MCPEXP005 // The SDK marks its Skills extension experimental.
        var packages = BundledAgentToolkitAssets.Load()
            .Where(asset => asset.Kind == AgentToolkitAssetKind.Skill)
            .GroupBy(asset => asset.RelativePath.Split('/', 2)[0], StringComparer.Ordinal)
            .Select(skill => McpServerSkillPackage.Create(
                skill.Select(asset => McpServerSkillFile.FromText(
                    asset.RelativePath[(skill.Key.Length + 1)..], asset.Content)),
                new McpServerSkillPackageCreateOptions { Uri = GetSkillUri(skill.Key) }));

        return builder.WithSkillPackages(packages);
#pragma warning restore MCPEXP005
    }

    internal static string GetSkillUri(string name) => $"skill://beutl/{Uri.EscapeDataString(name)}/SKILL.md";

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
            .WithTools<SceneTools>()
            .WithTools<VersionControlTools>()
            .WithTools<QueryTools>()
            .WithTools<EditTools>()
            .WithTools<TimelineEditTools>()
            .WithTools<ElementTemplateTools>()
            .WithTools<NodeGraphTools>()
            .WithTools<HistoryTools>()
            .WithTools<MissingMediaTools>()
            .WithTools<SubtitleTools>()
            .WithTools<RenderTools>();
    }
}
