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
using Beutl.Editor.Components.WebBrowserTab;
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
        Beutl.Editor.Components.NodeGraphTab.PropertyEditors.GenerativeChoicePropertyExtension.Instance,
        EqualizerPropertiesExtension.Instance,
        ScriptEditorExtension.Instance,
        FileBrowserTabExtension.Instance,
        AiWorkspaceTabExtension.Instance,
        HistoryTabExtension.Instance,
        TransitionTabExtension.Instance,
        VersionControlTabExtension.Instance,
        MissingMediaTabExtension.Instance,
        DockLayoutTabExtension.Instance,
        TerminalTabExtension.Instance,
        WebBrowserTabExtension.Instance,
        DarkBorderThemeExtension.Instance
    ];

    internal LoadPrimitiveExtensionTask(PackageManager manager, IExtensionRegistry provider,
        EditorService editorService, ProjectService projectService)
    {
        _manager = manager;
        // DefaultTutorialExtension needs the editor-session services, so unlike the other
        // primitive extensions it is not a service-free static singleton; build the full set here.
        Extension[] allExtensions =
            [.. PrimitiveExtensions, new DefaultTutorialExtension(editorService, projectService)];
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
                    var pkg = CreateEmbeddedPackage(
                        "FFmpeg",
                        "ffmpeg",
                        "decoder",
                        "decoding",
                        "encoder",
                        "encoding",
                        "video",
                        "audio");
                    try
                    {
                        var decoding = new Extensions.FFmpeg.Decoding.FFmpegDecodingExtension();
                        var encoding = new Extensions.FFmpeg.Encoding.FFmpegControlledEncodingExtension();
                        var propertyEditor = new Extensions.FFmpeg.PropertyEditors.FFmpegEncoderSpecializedPropertyExtension();
                        var proxy = new Extensions.FFmpeg.Proxy.FFmpegProxyExtension();
                        LoadEmbedded(
                            provider,
                            pkg,
                            [decoding, encoding, propertyEditor],
                            [decoding, encoding, propertyEditor, proxy]);
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
                    var pkg = CreateEmbeddedPackage(
                        "MediaFoundation",
                        "windows",
                        "media-foundation",
                        "decoder",
                        "decoding",
                        "encoder",
                        "encoding",
                        "video",
                        "audio");
                    try
                    {
                        var decoding = new Embedding.MediaFoundation.Decoding.MFDecodingExtension();
                        LoadEmbedded(provider, pkg, [decoding], [decoding]);
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
                    var pkg = CreateEmbeddedPackage(
                        "AVFoundation",
                        "macos",
                        "avfoundation",
                        "decoder",
                        "decoding",
                        "encoder",
                        "encoding",
                        "video",
                        "audio");
                    try
                    {
                        var decoding = new Extensions.AVFoundation.Decoding.AVFDecodingExtension();
                        var encoding = new Extensions.AVFoundation.Encoding.AVFEncodingExtension();
                        LoadEmbedded(provider, pkg, [decoding, encoding], [decoding, encoding]);
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

    // A media backend built into the app, registered under a package of its own.
    private static LocalPackage CreateEmbeddedPackage(string technology, params string[] tags)
    {
        var pkg = new LocalPackage
        {
            ShortDescription = $"{technology} for beutl",
            Name = $"Beutl.Embedding.{technology}",
            DisplayName = $"Beutl.Embedding.{technology}",
            InstalledPath = AppContext.BaseDirectory,
        };
        foreach (string tag in tags)
        {
            pkg.Tags.Add(tag);
        }

        pkg.Version = BeutlApplication.Version;
        pkg.WebSite = "https://github.com/b-editor/beutl";
        pkg.Publisher = "b-editor";
        return pkg;
    }

    // Settings are restored for the first list only, before any extension of the package loads.
    private void LoadEmbedded(
        IExtensionRegistry provider,
        LocalPackage pkg,
        IReadOnlyList<Extension> withSettings,
        IReadOnlyList<Extension> extensions)
    {
        foreach (Extension extension in withSettings)
        {
            _manager.SetupExtensionSettings(extension);
        }

        foreach (Extension extension in extensions)
        {
            extension.Load();
        }

        provider.AddExtensions(pkg.LocalId, extensions);
    }

    public override Task Task { get; }

    public List<(LocalPackage, Exception)> Failures { get; } = [];
}
