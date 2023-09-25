using Beutl.Api.Services;
using Beutl.Services.PrimitiveImpls;

namespace Beutl.Services.StartupTasks;

public sealed class LoadPrimitiveExtensionTask : StartupTask
{
    public static readonly Extension[] PrimitiveExtensions =
    {
        EditPageExtension.Instance,
        ExtensionsPageExtension.Instance,
        OutputPageExtension.Instance,
        SettingsPageExtension.Instance,
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
    };

    public LoadPrimitiveExtensionTask()
    {
        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("LoadPrimitiveExtensionTask"))
            {
                ExtensionProvider provider = ExtensionProvider.Current;
                foreach (Extension item in PrimitiveExtensions)
                {
                    item.Load();
                }
                provider.AddExtensions(LocalPackage.Reserved0, PrimitiveExtensions);
                activity?.AddEvent(new("Loaded_Extensions"));

                await Task.Yield();
#if FFMPEG_BUILD_IN
#pragma warning disable CS0436
                activity?.AddEvent(new("Loading_FFmpeg"));

                // Beutl.Extensions.FFmpeg.csproj
                var pkg = new LocalPackage
                {
                    ShortDescription = "FFmpeg for beutl",
                    Name = "Beutl.Embedding.FFmpeg",
                    DisplayName = "Beutl.Embedding.FFmpeg",
                    InstalledPath = AppContext.BaseDirectory,
                    Tags = { "ffmpeg", "decoder", "decoding", "encoder", "encoding", "video", "audio" },
                    Version = GitVersionInformation.NuGetVersionV2,
                    WebSite = "https://github.com/b-editor/beutl",
                    Publisher = "b-editor"
                };
                try
                {
                    var decoding = new Embedding.FFmpeg.Decoding.FFmpegDecodingExtension();
                    var encoding = new Embedding.FFmpeg.Encoding.FFmpegEncodingExtension();
                    decoding.Load();
                    encoding.Load();

                    provider.AddExtensions(pkg.LocalId, new Extension[]
                    {
                        decoding,
                        encoding
                    });
                }
                catch (Exception ex)
                {
                    Failures.Add((pkg, ex));
                }

                activity?.AddEvent(new("Loaded_FFmpeg"));
#pragma warning restore CS0436
#endif
            }
        });
    }

    public override Task Task { get; }

    public List<(LocalPackage, Exception)> Failures { get; } = new();
}
