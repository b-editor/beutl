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
                UICulture = { Value = CultureInfo.GetCultureInfo("ja-JP") },
                WindowPosition = { Value = (10, 20) },
                WindowSize = { Value = (800, 600) },
                IsWindowMaximized = { Value = true },
                UseCustomAccentColor = { Value = true },
                CustomAccentColor = { Value = "#FF112233" },
                ShowExactBoundaries = { Value = true },
                RecentFiles = { "/golden/a.belm" },
                RecentProjects = { "/golden/p.bproj" },
                PinnedToolTabs = { "Golden.Tab" },
                LastOpenedProjectFile = { Value = "/golden/p.bproj" },
            }),
            ["Telemetry"] = CoreSerializer.SerializeToJsonObject(new TelemetryConfig
            {
                Beutl_Logging = { Value = true },
                Beutl_Application = { Value = false },
            }),
            ["Editor"] = CoreSerializer.SerializeToJsonObject(new EditorConfig
            {
                LastMediaDirectory = { Value = "/golden/media" },
                AutoAdjustSceneDuration = { Value = false },
                FrameCacheMaxSize = { Value = 512 },
                FrameCacheScale = { Value = FrameCacheConfigScale.Original },
                TimelineAutoScrollMode = { Value = TimelineAutoScrollMode.AlwaysFollow },
                ToneMappingMode = { Value = UIToneMappingOperator.ACES },
                ToneMappingExposure = { Value = 0.5f },
                OnionSkinPrevCount = { Value = 2 },
                OnionSkinPrevOpacity = { Value = 0.25f },
                PreviewSourceMode = { Value = PreviewSourceMode.ForceOriginal },
            }),
            ["Graphics"] = CoreSerializer.SerializeToJsonObject(new GraphicsConfig { SelectedGpuName = { Value = "Golden GPU" } }),
            ["Tutorial"] = CoreSerializer.SerializeToJsonObject(new TutorialConfig { CompletedTutorialIds = { Value = ["golden"] } }),
            ["AiAgent"] = CoreSerializer.SerializeToJsonObject(new AiAgentConfig
            {
                AgentId = { Value = "golden" },
                InstallScope = { Value = "user" },
                ProjectRoot = { Value = "/golden" },
                InstallSubagents = { Value = false },
                FollowLiveMcpEdits = { Value = true },
            }),
            ["ProxyStore"] = CoreSerializer.SerializeToJsonObject(new ProxyStoreConfig
            {
                StoreRootPath = { Value = "/golden/proxies" },
                MaxTotalBytes = { Value = 1024 },
                DefaultPreset = { Value = 1 },
            }),
            ["VersionControl"] = CoreSerializer.SerializeToJsonObject(new VersionControlConfig
            {
                EnableForNewProjects = { Value = false },
                GitExecutablePath = { Value = "/golden/git" },
                LargeMediaWarningThresholdMb = { Value = 10 },
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
                Assert.That(config.ViewConfig.UICulture.Value, Is.EqualTo(CultureInfo.GetCultureInfo("ja-JP")));
                Assert.That(config.ViewConfig.ShowExactBoundaries.Value, Is.True);
                Assert.That(config.TelemetryConfig.Beutl_Logging.Value, Is.True);
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
            SourceSize = { Value = new PixelSize(1920, 1080) },
            DestinationSize = { Value = new PixelSize(1280, 720) },
            FrameRate = { Value = new Rational(24, 1) },
            Bitrate = { Value = 8_000_000 },
            KeyframeRate = { Value = 48 },
            ColorPrimaries = { Value = FFColorPrimaries.BT709 },
            ColorRange = { Value = FFColorRange.MPEG },
        };
        video.Options.Value.Add(new AdditionalOption("tune", "film"));
        var json = new JsonObject
        {
            ["Video"] = CoreSerializer.SerializeToJsonObject(video),
            ["Audio"] = CoreSerializer.SerializeToJsonObject(new FFmpegAudioEncoderSettings
            {
                SampleRate = { Value = 48000 },
                Channels = { Value = 1 },
                Bitrate = { Value = 192_000 },
            }),
            ["Encoding"] = CoreSerializer.SerializeToJsonObject(new FFmpegEncodingSettings
            {
                ThreadCount = { Value = 4 },
                Acceleration = { Value = FFmpegEncodingSettings.AccelerationOptions.Auto },
            }),
            ["Decoding"] = CoreSerializer.SerializeToJsonObject(new FFmpegDecodingSettings
            {
                ThreadCount = { Value = 2 },
                Acceleration = { Value = FFmpegDecodingSettings.AccelerationOptions.Auto },
                ForceSrgbGamma = { Value = false },
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
