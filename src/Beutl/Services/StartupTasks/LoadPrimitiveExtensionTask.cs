using Beutl.Api.Services;
using Beutl.Editor.Components.ColorScopesTab;
using Beutl.Editor.Components.CurvesTab;
using Beutl.Editor.Components.ColorGradingProperties;
using Beutl.Editor.Components.ColorGradingTab;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Editor.Components.ObjectPropertyTab;
using Beutl.Editor.Components.SceneSettingsTab;
using Beutl.Editor.Components.SourceOperatorsTab;
using Beutl.Services.PrimitiveImpls;

namespace Beutl.Services.StartupTasks;

public sealed class LoadPrimitiveExtensionTask : StartupTask
{
    private readonly PackageManager _manager;

    public static readonly Extension[] PrimitiveExtensions =
    [
        ExtensionsPageExtension.Instance,
        OutputTabExtension.Instance,
        SceneEditorExtension.Instance,
        SceneOutputExtension.Instance,
        SceneProjectItemExtension.Instance,
        TimelineTabExtension.Instance,
        ObjectPropertyTabExtension.Instance,
        SourceOperatorsTabExtension.Instance,
        PropertyEditorExtension.Instance,
        NodeTreeTabExtension.Instance,
        NodeTreeInputTabExtension.Instance,
        GraphEditorTabExtension.Instance,
        SceneSettingsTabExtension.Instance,
        WaveReaderExtension.Instance,
        PathEditorTabExtension.Instance,
        LibraryTabExtension.Instance,
        AnimatedImageReaderExtension.Instance,
        AnimatedPngReaderExtension.Instance,
        MainViewExtension.Instance,
        ColorScopesTabExtension.Instance,
        ColorGradingTabExtension.Instance,
        CurvesTabExtension.Instance,
        ColorGradingPropertiesExtension.Instance,
        FileBrowserTabExtension.Instance,
    ];

    public LoadPrimitiveExtensionTask(PackageManager manager)
    {
        _manager = manager;
        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("LoadPrimitiveExtensionTask"))
            {
                ExtensionProvider provider = ExtensionProvider.Current;
                foreach (Extension item in PrimitiveExtensions)
                {
                    _manager.SetupExtensionSettings(item);
                    if (item is ViewExtension viewExtension)
                    {
                        manager.ContextCommandManager.Register(viewExtension);
                    }
                    item.Load();
                }

                provider.AddExtensions(LocalPackage.Reserved0, PrimitiveExtensions);
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
                        var decoding = new Embedding.FFmpeg.Decoding.FFmpegDecodingExtension();
                        var encoding = new Embedding.FFmpeg.Encoding.FFmpegControlledEncodingExtension();
                        _manager.SetupExtensionSettings(decoding);
                        _manager.SetupExtensionSettings(encoding);
                        decoding.Load();
                        encoding.Load();

                        provider.AddExtensions(pkg.LocalId, [decoding, encoding]);
                    }
                    catch (Exception ex)
                    {
                        Failures.Add((pkg, ex));
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
                        _manager.SetupExtensionSettings(decoding);
                        decoding.Load();

                        provider.AddExtensions(pkg.LocalId, [decoding]);
                    }
                    catch (Exception ex)
                    {
                        Failures.Add((pkg, ex));
                    }

                    activity?.AddEvent(new("Loaded_MediaFoundation"));
                }
#pragma warning restore CS0436
#endif

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
                        _manager.SetupExtensionSettings(decoding);
                        _manager.SetupExtensionSettings(encoding);
                        decoding.Load();
                        encoding.Load();

                        provider.AddExtensions(pkg.LocalId, [decoding, encoding]);
                    }
                    catch (Exception ex)
                    {
                        Failures.Add((pkg, ex));
                    }

                    activity?.AddEvent(new("Loaded_AVFoundation"));
                }
#pragma warning restore CS0436
            }
        });
    }

    public override Task Task { get; }

    public List<(LocalPackage, Exception)> Failures { get; } = [];
}
