using Beutl.Api.Services;
using Beutl.Editor.Components.AudioVisualizerTab;
using Beutl.Editor.Components.ColorGradingProperties;
using Beutl.Editor.Components.ColorGradingTab;
using Beutl.Editor.Components.ColorScopesTab;
using Beutl.Editor.Components.CurvesTab;
using Beutl.Editor.Components.ElementPropertyTab;
using Beutl.Editor.Components.EqualizerProperties;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.GraphEditorTab;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Editor.Components.NodeGraphTab;
using Beutl.Editor.Components.ObjectPropertyTab;
using Beutl.Editor.Components.PathEditorTab;
using Beutl.Editor.Components.PreviewSettingsTab;
using Beutl.Editor.Components.ProxiesTab;
using Beutl.Editor.Components.SceneSettingsTab;
using Beutl.Editor.Components.TerminalTab;
using Beutl.Logging;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class LoadPrimitiveExtensionTask : StartupTask
{
    private readonly ILogger<LoadPrimitiveExtensionTask> _logger = Log.CreateLogger<LoadPrimitiveExtensionTask>();
    private readonly PackageManager _manager;

    public static readonly Extension[] PrimitiveExtensions =
    [
        ExtensionsToolWindowExtension.Instance,
        OutputTabExtension.Instance,
        SceneEditorExtension.Instance,
        SceneOutputExtension.Instance,
        SceneProjectItemExtension.Instance,
        TimelineTabExtension.Instance,
        ObjectPropertyTabExtension.Instance,
        ElementPropertyTabExtension.Instance,
        PropertyEditorExtension.Instance,
        NodeGraphTabExtension.Instance,
        GraphEditorTabExtension.Instance,
        SceneSettingsTabExtension.Instance,
        PreviewSettingsTabExtension.Instance,
        ProxiesTabExtension.Instance,
        WaveReaderExtension.Instance,
        PathEditorTabExtension.Instance,
        LibraryTabExtension.Instance,
        AnimatedImageReaderExtension.Instance,
        AnimatedPngReaderExtension.Instance,
        MainViewExtension.Instance,
        ColorScopesTabExtension.Instance,
        AudioVisualizerTabExtension.Instance,
        ColorGradingTabExtension.Instance,
        CurvesTabExtension.Instance,
        ColorGradingPropertiesExtension.Instance,
        EqualizerPropertiesExtension.Instance,
        ScriptEditorExtension.Instance,
        FileBrowserTabExtension.Instance,
        HistoryTabExtension.Instance,
        DockLayoutTabExtension.Instance,
        TerminalTabExtension.Instance,
        DarkBorderThemeExtension.Instance
    ];

    public LoadPrimitiveExtensionTask(PackageManager manager, ExtensionProvider provider,
        EditorService editorService, ProjectService projectService)
    {
        _manager = manager;
        // DefaultTutorialExtension needs the editor-session services, so unlike the other
        // primitive extensions it is not a service-free static singleton; build the full set here.
        Extension[] allExtensions =
            [.. PrimitiveExtensions, new DefaultTutorialExtension(editorService, projectService)];
        BuiltInFeatureCatalog.Register(allExtensions);
        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("LoadPrimitiveExtensionTask"))
            {
                foreach (Extension item in allExtensions)
                {
                    _manager.SetupExtensionSettings(item);
                    if (item is ViewExtension viewExtension)
                    {
                        manager.ContextCommandManager.Register(viewExtension);
                    }

                    item.Load();
                }

                provider.AddExtensions(LocalPackage.Reserved0, allExtensions);
                activity?.AddEvent(new("Loaded_Extensions"));

                await Task.Yield();
#if FFMPEG_BUILD_IN
#pragma warning disable CS0436
                {
                    activity?.AddEvent(new("Loading_FFmpeg"));

                    // Beutl.Extensions.FFmpeg.csproj
                    var pkg = new LocalPackage
                    {
                        ShortDescription = "FFmpeg for beutl",
                        Name = "Beutl.Embedding.FFmpeg",
                        DisplayName = "Beutl.Embedding.FFmpeg",
                        InstalledPath = AppContext.BaseDirectory,
                        Tags =
                        {
                            "ffmpeg",
                            "decoder",
                            "decoding",
                            "encoder",
                            "encoding",
                            "video",
                            "audio"
                        },
                        Version = BeutlApplication.Version,
                        WebSite = "https://github.com/b-editor/beutl",
                        Publisher = "b-editor"
                    };
                    try
                    {
                        var decoding = new Extensions.FFmpeg.Decoding.FFmpegDecodingExtension();
                        var encoding = new Extensions.FFmpeg.Encoding.FFmpegControlledEncodingExtension();
                        var propertyEditor = new Extensions.FFmpeg.PropertyEditors.FFmpegEncoderSpecializedPropertyExtension();
                        var proxy = new Extensions.FFmpeg.Proxy.FFmpegProxyExtension();
                        BuiltInFeatureCatalog.Register([decoding, encoding, propertyEditor, proxy]);
                        _manager.SetupExtensionSettings(decoding);
                        _manager.SetupExtensionSettings(encoding);
                        _manager.SetupExtensionSettings(propertyEditor);
                        decoding.Load();
                        encoding.Load();
                        propertyEditor.Load();
                        proxy.Load();

                        provider.AddExtensions(pkg.LocalId, [decoding, encoding, propertyEditor, proxy]);
                    }
                    catch (Exception ex)
                    {
                        Failures.Add((pkg, ex));
                        _logger.LogError(ex, "Failed to load FFmpeg extensions for package {Package}", pkg.Name);
                    }

                    activity?.AddEvent(new("Loaded_FFmpeg"));
                }
#pragma warning restore CS0436
#endif

#if MF_BUILD_IN
#pragma warning disable CS0436
                if (OperatingSystem.IsWindows())
                {
                    activity?.AddEvent(new("Loading_MediaFoundation"));

                    // Beutl.Extensions.FFmpeg.csproj
                    var pkg = new LocalPackage
                    {
                        ShortDescription = "MediaFoundation for beutl",
                        Name = "Beutl.Embedding.MediaFoundation",
                        DisplayName = "Beutl.Embedding.MediaFoundation",
                        InstalledPath = AppContext.BaseDirectory,
                        Tags =
 { "windows", "media-foundation", "decoder", "decoding", "encoder", "encoding", "video", "audio" },
                        Version = BeutlApplication.Version,
                        WebSite = "https://github.com/b-editor/beutl",
                        Publisher = "b-editor"
                    };
                    try
                    {
                        var decoding = new Embedding.MediaFoundation.Decoding.MFDecodingExtension();
                        BuiltInFeatureCatalog.Register([decoding]);
                        _manager.SetupExtensionSettings(decoding);
                        decoding.Load();

                        provider.AddExtensions(pkg.LocalId, [decoding]);
                    }
                    catch (Exception ex)
                    {
                        Failures.Add((pkg, ex));
                        _logger.LogError(ex, "Failed to load MediaFoundation extensions for package {Package}", pkg.Name);
                    }

                    activity?.AddEvent(new("Loaded_MediaFoundation"));
                }
#pragma warning restore CS0436
#endif

#if AVF_BUILD_IN
#pragma warning disable CS0436
                if (OperatingSystem.IsMacOS())
                {
                    activity?.AddEvent(new("Loading_AVFoundation"));

                    // Beutl.Extensions.FFmpeg.csproj
                    var pkg = new LocalPackage
                    {
                        ShortDescription = "AVFoundation for beutl",
                        Name = "Beutl.Embedding.AVFoundation",
                        DisplayName = "Beutl.Embedding.AVFoundation",
                        InstalledPath = AppContext.BaseDirectory,
                        Tags =
                        {
                            "macos",
                            "avfoundation",
                            "decoder",
                            "decoding",
                            "encoder",
                            "encoding",
                            "video",
                            "audio"
                        },
                        Version = BeutlApplication.Version,
                        WebSite = "https://github.com/b-editor/beutl",
                        Publisher = "b-editor"
                    };
                    try
                    {
                        var decoding = new Extensions.AVFoundation.Decoding.AVFDecodingExtension();
                        var encoding = new Extensions.AVFoundation.Encoding.AVFEncodingExtension();
                        BuiltInFeatureCatalog.Register([decoding, encoding]);
                        _manager.SetupExtensionSettings(decoding);
                        _manager.SetupExtensionSettings(encoding);
                        decoding.Load();
                        encoding.Load();

                        provider.AddExtensions(pkg.LocalId, [decoding, encoding]);
                    }
                    catch (Exception ex)
                    {
                        Failures.Add((pkg, ex));
                        _logger.LogError(ex, "Failed to load AVFoundation extensions for package {Package}", pkg.Name);
                    }

                    activity?.AddEvent(new("Loaded_AVFoundation"));
                }
#pragma warning restore CS0436
#endif
            }
        });
    }

    public override Task Task { get; }

    public List<(LocalPackage, Exception)> Failures { get; } = [];
}

internal static class BuiltInFeatureCatalog
{
    private static readonly IReadOnlyDictionary<Type, string> s_featureIds = new Dictionary<Type, string>
    {
        [typeof(ExtensionsToolWindowExtension)] = "builtin/tool-window/extensions",
        [typeof(OutputTabExtension)] = "builtin/tool-tab/output",
        [typeof(SceneEditorExtension)] = "builtin/editor/scene",
        [typeof(SceneOutputExtension)] = "builtin/output/scene",
        [typeof(SceneProjectItemExtension)] = "builtin/project-item/scene",
        [typeof(TimelineTabExtension)] = "builtin/tool-tab/timeline",
        [typeof(ObjectPropertyTabExtension)] = "builtin/tool-tab/object-properties",
        [typeof(ElementPropertyTabExtension)] = "builtin/tool-tab/element-properties",
        [typeof(PropertyEditorExtension)] = "builtin/property-editor/default",
        [typeof(NodeGraphTabExtension)] = "builtin/tool-tab/node-graph",
        [typeof(GraphEditorTabExtension)] = "builtin/tool-tab/graph-editor",
        [typeof(SceneSettingsTabExtension)] = "builtin/tool-tab/scene-settings",
        [typeof(PreviewSettingsTabExtension)] = "builtin/tool-tab/preview-settings",
        [typeof(ProxiesTabExtension)] = "builtin/tool-tab/proxies",
        [typeof(WaveReaderExtension)] = "builtin/decoder/wave",
        [typeof(PathEditorTabExtension)] = "builtin/tool-tab/path-editor",
        [typeof(LibraryTabExtension)] = "builtin/tool-tab/library",
        [typeof(AnimatedImageReaderExtension)] = "builtin/decoder/animated-image",
        [typeof(AnimatedPngReaderExtension)] = "builtin/decoder/animated-png",
        [typeof(MainViewExtension)] = "builtin/view/main",
        [typeof(ColorScopesTabExtension)] = "builtin/tool-tab/color-scopes",
        [typeof(AudioVisualizerTabExtension)] = "builtin/tool-tab/audio-visualizer",
        [typeof(ColorGradingTabExtension)] = "builtin/tool-tab/color-grading",
        [typeof(CurvesTabExtension)] = "builtin/tool-tab/curves",
        [typeof(ColorGradingPropertiesExtension)] = "builtin/property-editor/color-grading",
        [typeof(EqualizerPropertiesExtension)] = "builtin/property-editor/equalizer",
        [typeof(ScriptEditorExtension)] = "builtin/property-editor/script",
        [typeof(FileBrowserTabExtension)] = "builtin/tool-tab/file-browser",
        [typeof(HistoryTabExtension)] = "builtin/tool-tab/history",
        [typeof(DockLayoutTabExtension)] = "builtin/tool-tab/dock-layout",
        [typeof(TerminalTabExtension)] = "builtin/tool-tab/terminal",
        [typeof(DarkBorderThemeExtension)] = "builtin/theme/dark-border",
        [typeof(DefaultTutorialExtension)] = "builtin/tutorial/default",
#if FFMPEG_BUILD_IN
        [typeof(Extensions.FFmpeg.Decoding.FFmpegDecodingExtension)] = "builtin/decoder/ffmpeg",
        [typeof(Extensions.FFmpeg.Encoding.FFmpegControlledEncodingExtension)] = "builtin/encoder/ffmpeg",
        [typeof(Extensions.FFmpeg.PropertyEditors.FFmpegEncoderSpecializedPropertyExtension)] = "builtin/property-editor/ffmpeg",
        [typeof(Extensions.FFmpeg.Proxy.FFmpegProxyExtension)] = "builtin/proxy/ffmpeg",
#endif
#if MF_BUILD_IN
        [typeof(Embedding.MediaFoundation.Decoding.MFDecodingExtension)] = "builtin/decoder/media-foundation",
#endif
#if AVF_BUILD_IN
        [typeof(Extensions.AVFoundation.Decoding.AVFDecodingExtension)] = "builtin/decoder/av-foundation",
        [typeof(Extensions.AVFoundation.Encoding.AVFEncodingExtension)] = "builtin/encoder/av-foundation",
#endif
    };

    internal static IReadOnlyDictionary<Type, string> FeatureIds => s_featureIds;

    internal static void Register(IEnumerable<Extension> extensions)
    {
        foreach (Extension extension in extensions)
        {
            Register(extension.GetType());
        }
    }

    internal static void Register(Type type)
    {
        if (s_featureIds.TryGetValue(type, out string? featureId))
        {
            Telemetry.RegisterBuiltInFeature(type, featureId);
        }
    }

    internal static bool TryGetFeatureId(Type type, out string? featureId)
    {
        return s_featureIds.TryGetValue(type, out featureId);
    }
}
