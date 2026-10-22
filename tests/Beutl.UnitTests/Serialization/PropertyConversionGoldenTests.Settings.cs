using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.UnitTests.Serialization;

// The settings and the encoder settings, whose CoreProperty members become IProperty in #2884 step 9.
public partial class PropertyConversionGoldenTests
{
    [Test]
    public void TheSettingsSections()
    {
        // In the order GlobalConfiguration writes them, each with values that do not depend on the machine.
        var json = new JsonObject
        {
            ["View"] = CoreSerializer.SerializeToJsonObject(new ViewConfig
            {
                UICulture = CultureInfo.GetCultureInfo("ja-JP"),
                WindowPosition = (10, 20),
                WindowSize = (800, 600),
                IsWindowMaximized = true,
                UseCustomAccentColor = true,
                CustomAccentColor = "#FF112233",
                ShowExactBoundaries = true,
                RecentFiles = ["/golden/a.belm"],
                RecentProjects = ["/golden/p.bproj"],
                PinnedToolTabs = ["Golden.Tab"],
                LastOpenedProjectFile = "/golden/p.bproj",
            }),
            ["Telemetry"] = CoreSerializer.SerializeToJsonObject(new TelemetryConfig
            {
                Beutl_Logging = true,
                Beutl_Application = false,
            }),
            ["Editor"] = CoreSerializer.SerializeToJsonObject(new EditorConfig
            {
                LastMediaDirectory = "/golden/media",
                AutoAdjustSceneDuration = false,
                FrameCacheMaxSize = 512,
                FrameCacheScale = FrameCacheConfigScale.Original,
                TimelineAutoScrollMode = TimelineAutoScrollMode.AlwaysFollow,
                ToneMappingMode = UIToneMappingOperator.ACES,
                ToneMappingExposure = 0.5f,
                OnionSkinPrevCount = 2,
                OnionSkinPrevOpacity = 0.25f,
                PreviewSourceMode = PreviewSourceMode.ForceOriginal,
            }),
            ["Graphics"] = CoreSerializer.SerializeToJsonObject(new GraphicsConfig { SelectedGpuName = "Golden GPU" }),
            ["Tutorial"] = CoreSerializer.SerializeToJsonObject(new TutorialConfig { CompletedTutorialIds = ["golden"] }),
            ["AiAgent"] = CoreSerializer.SerializeToJsonObject(new AiAgentConfig
            {
                AgentId = "golden",
                InstallScope = "user",
                ProjectRoot = "/golden",
                InstallSubagents = false,
                FollowLiveMcpEdits = true,
            }),
            ["ProxyStore"] = CoreSerializer.SerializeToJsonObject(new ProxyStoreConfig
            {
                StoreRootPath = "/golden/proxies",
                MaxTotalBytes = 1024,
                DefaultPreset = 1,
            }),
            ["VersionControl"] = CoreSerializer.SerializeToJsonObject(new VersionControlConfig
            {
                EnableForNewProjects = false,
                GitExecutablePath = "/golden/git",
                LargeMediaWarningThresholdMb = 10,
            }),
        };

        AssertGolden(json, "settings");
    }

    [Test]
    public void TheLowerCaseSectionsOfEarlyBuildsAreRead()
    {
        // Builds before 1.0.0-preview.2 wrote these sections under lower-case keys (GlobalConfiguration's LegacyKey).
        string directory = Directory.CreateTempSubdirectory("beutl-golden-settings").FullName;
        try
        {
            string path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, new JsonObject
            {
                ["view"] = new JsonObject { ["UICulture"] = "ja-JP", ["ShowExactBoundaries"] = true },
                ["telemetry"] = new JsonObject { ["Beutl_Logging"] = true },
            }.ToJsonString());
            var config = (GlobalConfiguration)Activator.CreateInstance(typeof(GlobalConfiguration), nonPublic: true)!;
            config.Restore(path);

            Assert.Multiple(() =>
            {
                Assert.That(config.ViewConfig.UICulture, Is.EqualTo(CultureInfo.GetCultureInfo("ja-JP")));
                Assert.That(config.ViewConfig.ShowExactBoundaries, Is.True);
                Assert.That(config.TelemetryConfig.Beutl_Logging, Is.True);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void TheEncoderSettings()
    {
        var video = new FFmpegVideoEncoderSettings
        {
            SourceSize = new PixelSize(1920, 1080),
            DestinationSize = new PixelSize(1280, 720),
            FrameRate = new Rational(24, 1),
            Bitrate = 8_000_000,
            KeyframeRate = 48,
            ColorPrimaries = FFColorPrimaries.BT709,
            ColorRange = FFColorRange.MPEG,
        };
        video.Options.Add(new AdditionalOption("tune", "film"));
        var json = new JsonObject
        {
            ["Video"] = CoreSerializer.SerializeToJsonObject(video),
            ["Audio"] = CoreSerializer.SerializeToJsonObject(new FFmpegAudioEncoderSettings
            {
                SampleRate = 48000,
                Channels = 1,
                Bitrate = 192_000,
            }),
            ["Encoding"] = CoreSerializer.SerializeToJsonObject(new FFmpegEncodingSettings
            {
                ThreadCount = 4,
                Acceleration = FFmpegEncodingSettings.AccelerationOptions.Auto,
            }),
            ["Decoding"] = CoreSerializer.SerializeToJsonObject(new FFmpegDecodingSettings
            {
                ThreadCount = 2,
                Acceleration = FFmpegDecodingSettings.AccelerationOptions.Auto,
                ForceSrgbGamma = false,
            }),
        };

        AssertGolden(json, "encoder-settings");
    }

    // Each saved section reads back into new settings that write the same JSON.
    [TestCase("settings", "View", typeof(ViewConfig))]
    [TestCase("settings", "Telemetry", typeof(TelemetryConfig))]
    [TestCase("settings", "Editor", typeof(EditorConfig))]
    [TestCase("settings", "Graphics", typeof(GraphicsConfig))]
    [TestCase("settings", "Tutorial", typeof(TutorialConfig))]
    [TestCase("settings", "AiAgent", typeof(AiAgentConfig))]
    [TestCase("settings", "ProxyStore", typeof(ProxyStoreConfig))]
    [TestCase("settings", "VersionControl", typeof(VersionControlConfig))]
    [TestCase("encoder-settings", "Video", typeof(FFmpegVideoEncoderSettings))]
    [TestCase("encoder-settings", "Audio", typeof(FFmpegAudioEncoderSettings))]
    [TestCase("encoder-settings", "Encoding", typeof(FFmpegEncodingSettings))]
    [TestCase("encoder-settings", "Decoding", typeof(FFmpegDecodingSettings))]
    public void EachSettingsSectionReadsBack(string name, string section, Type type)
    {
        var expected = (JsonObject)ReadGolden(name)[section]!;
        var settings = (ICoreSerializable)Activator.CreateInstance(type)!;

        CoreSerializer.PopulateFromJsonObject(settings, type, (JsonObject)expected.DeepClone());

        Assert.That(CoreSerializer.SerializeToJsonObject(settings).ToJsonString(), Is.EqualTo(expected.ToJsonString()));
    }

    private static JsonObject ReadGolden(string name, [CallerFilePath] string source = "")
        => (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(source)!, "Golden", $"{name}.json")))!;
}
