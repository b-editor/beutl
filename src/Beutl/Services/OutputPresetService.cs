using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Helpers;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Services;

public class OutputPresetItem(OutputExtension extension, JsonObject json, string name)
{
    public OutputExtension Extension { get; } = extension;

    public JsonObject Json { get; } = json;

    public ReactiveProperty<string> Name { get; } = new(name);

    public void Apply(ISupportOutputPreset context)
    {
        if (((IOutputContext)context).Extension.GetType() != Extension.GetType())
        {
            throw new InvalidOperationException("Extension type mismatch.");
        }

        context.Apply(Json);
    }

    public static OutputPresetItem CreateFromContext(ISupportOutputPreset context, string name)
    {
        return new OutputPresetItem(
            ((IOutputContext)context).Extension,
            context.ToPreset(),
            name);
    }

    public static JsonNode ToJson(OutputPresetItem item)
    {
        return new JsonObject
        {
            [nameof(Extension)] = TypeFormat.ToString(item.Extension.GetType()),
            [nameof(Json)] = item.Json.DeepClone(),
            [nameof(Name)] = item.Name.Value
        };
    }

    public static OutputPresetItem? FromJson(JsonNode json, ILogger logger)
    {
        try
        {
            string? typeName = json[nameof(Extension)]?.ToString();
            if (typeName == null)
            {
                logger.LogError("Extension type name is null.");
                return null;
            }

            Type? type = TypeFormat.ToType(typeName);
            if (type == null)
            {
                logger.LogError("Extension type not found: {TypeName}", typeName);
                return null;
            }

            if (Activator.CreateInstance(type) is not OutputExtension extension)
            {
                logger.LogError("Failed to create instance of extension type: {TypeName}", typeName);
                return null;
            }

            if (json[nameof(Json)] is not JsonObject jsonObject)
            {
                logger.LogError("Json object is null.");
                return null;
            }

            if (json[nameof(Name)] is not JsonValue jsonValue || !jsonValue.TryGetValue(out string? name))
            {
                logger.LogError("Name is null.");
                return null;
            }

            return new OutputPresetItem(extension, jsonObject, name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An exception has occurred while creating OutputPresetItem from JSON.");
            return null;
        }
    }
}

public sealed class OutputPresetService
{
    public static readonly OutputPresetService Instance = new();
    private readonly CoreList<OutputPresetItem> _items = [];

    private readonly string _filePath = Path.Combine(
        BeutlEnvironment.GetHomeDirectoryPath(), "output-presets.json");

    private readonly ILogger _logger = Log.CreateLogger<OutputPresetService>();
    private bool _isRestored;

    private OutputPresetService()
    {
        RestoreItems();
    }

    public ICoreList<OutputPresetItem> Items => _items;

    public void AddItem(IOutputContext context, string name)
    {
        if (context is not ISupportOutputPreset outputPreset) return;
        var item = OutputPresetItem.CreateFromContext(outputPreset, name);
        Items.Add(item);
        _logger.LogInformation("Added new OutputPresetItem. Context: {Context}", context);
    }

    public void SaveItems()
    {
        if (!_isRestored) return;

        var array = new JsonArray();
        foreach (OutputPresetItem item in _items)
        {
            JsonNode json = OutputPresetItem.ToJson(item);
            array.Add(json);
        }

        using FileStream stream = File.Create(_filePath);
        using var writer = new Utf8JsonWriter(stream);
        array.WriteTo(writer);
        _logger.LogInformation("Saved {Count} OutputPresetItem to file: {FilePath}", _items.Count, _filePath);
    }

    public void RestoreItems()
    {
        try
        {
            _isRestored = true;
            if (!File.Exists(_filePath))
            {
                _logger.LogWarning("Output preset file not found: {FilePath}", _filePath);
                return;
            }

            using FileStream stream = File.Open(_filePath, FileMode.Open);
            var jsonNode = JsonNode.Parse(stream);
            if (jsonNode is not JsonArray jsonArray)
            {
                _logger.LogWarning("Invalid JSON format in output preset file: {FilePath}", _filePath);
                return;
            }

            _items.Clear();

            _items.EnsureCapacity(jsonArray.Count);

            foreach (JsonNode? jsonItem in jsonArray)
            {
                if (jsonItem == null) continue;

                var item = OutputPresetItem.FromJson(jsonItem, _logger);
                if (item != null)
                {
                    _items.Add(item);
                }
            }

            _logger.LogInformation("Restored {Count} OutputPresetItem from file: {FilePath}", _items.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception has occurred while restoring output preset file.");
        }
        finally
        {
            if (_items.Count == 0)
            {
                CreateDefaultPresets();
            }
        }
    }

    private void CreateDefaultPresets()
    {
        var vid = new FFmpegVideoEncoderSettings();
        vid.Format = FFPixelFormat.YUV420P;
        vid.Bitrate = 15000000;
        vid.KeyframeRate = 12;
        vid.Codec = VideoCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == "libx264") ?? CodecRecord.Default;
        vid.Options.Clear();
        vid.Options.AddRange(
        [
            new AdditionalOption("preset", "veryslow"),
            new AdditionalOption("crf", "18"),
            new AdditionalOption("profile", "high"),
            new AdditionalOption("level", "4.0"),
        ]);
        var aud = new FFmpegAudioEncoderSettings();
        aud.Bitrate = 320000;
        aud.Codec = AudioCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == "aac") ?? CodecRecord.Default;

        _items.Add(new OutputPresetItem(
            SceneOutputExtension.Instance,
            new JsonObject
            {
                ["SelectedEncoder"] = TypeFormat.ToString(typeof(FFmpegControlledEncodingExtension)),
                ["VideoSettings"] = CoreSerializer.SerializeToJsonObject(vid),
                ["AudioSettings"] = CoreSerializer.SerializeToJsonObject(aud)
            },
            "High Quality"));

        ObjectRegenerator.Regenerate<FFmpegVideoEncoderSettings>(vid, out vid);
        ObjectRegenerator.Regenerate<FFmpegAudioEncoderSettings>(aud, out aud);

        vid.Bitrate = 8000000;
        vid.KeyframeRate = 30;
        vid.Options.First(i => i.Name == "preset").Value = "medium";
        vid.Options.First(i => i.Name == "crf").Value = "23";
        aud.Bitrate = 128000;

        _items.Add(new OutputPresetItem(
            SceneOutputExtension.Instance,
            new JsonObject
            {
                ["SelectedEncoder"] = TypeFormat.ToString(typeof(FFmpegControlledEncodingExtension)),
                ["VideoSettings"] = CoreSerializer.SerializeToJsonObject(vid),
                ["AudioSettings"] = CoreSerializer.SerializeToJsonObject(aud)
            },
            "Medium Quality"));

        ObjectRegenerator.Regenerate<FFmpegVideoEncoderSettings>(vid, out vid);
        ObjectRegenerator.Regenerate<FFmpegAudioEncoderSettings>(aud, out aud);

        vid.Bitrate = 3000000;
        vid.KeyframeRate = 60;
        vid.Options.First(i => i.Name == "preset").Value = "ultrafast";
        vid.Options.First(i => i.Name == "crf").Value = "28";
        aud.Bitrate = 128000;

        _items.Add(new OutputPresetItem(
            SceneOutputExtension.Instance,
            new JsonObject
            {
                ["SelectedEncoder"] = TypeFormat.ToString(typeof(FFmpegControlledEncodingExtension)),
                ["VideoSettings"] = CoreSerializer.SerializeToJsonObject(vid),
                ["AudioSettings"] = CoreSerializer.SerializeToJsonObject(aud)
            },
            "Low Quality"));

        // HDR10 (PQ)
        ObjectRegenerator.Regenerate<FFmpegVideoEncoderSettings>(vid, out vid);
        ObjectRegenerator.Regenerate<FFmpegAudioEncoderSettings>(aud, out aud);

        vid.Format = FFPixelFormat.YUV420P10LE;
        vid.Bitrate = 20000000;
        vid.KeyframeRate = 24;
        vid.Codec = VideoCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == "libx265") ?? CodecRecord.Default;
        vid.ColorPrimaries = FFColorPrimaries.BT2020;
        vid.ColorTrc = FFColorTransfer.SMPTE2084;
        vid.ColorSpace = FFColorSpace.BT2020_NCL;
        vid.ColorRange = FFColorRange.MPEG;
        vid.Options.Clear();
        vid.Options.AddRange(
        [
            new AdditionalOption("preset", "medium"),
            new AdditionalOption("crf", "20"),
            new AdditionalOption("profile", "main10"),
            new AdditionalOption("x265-params",
                "colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:range=limited"
                + ":master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400:repeat-headers=1"),
        ]);
        aud.Bitrate = 192000;
        aud.Codec = AudioCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == "aac") ?? CodecRecord.Default;

        _items.Add(new OutputPresetItem(
            SceneOutputExtension.Instance,
            new JsonObject
            {
                ["SelectedEncoder"] = TypeFormat.ToString(typeof(FFmpegControlledEncodingExtension)),
                ["VideoSettings"] = CoreSerializer.SerializeToJsonObject(vid),
                ["AudioSettings"] = CoreSerializer.SerializeToJsonObject(aud)
            },
            "HDR10 (PQ)"));

        // HLG
        ObjectRegenerator.Regenerate<FFmpegVideoEncoderSettings>(vid, out vid);
        ObjectRegenerator.Regenerate<FFmpegAudioEncoderSettings>(aud, out aud);

        vid.Format = FFPixelFormat.YUV420P10LE;
        vid.Bitrate = 20000000;
        vid.KeyframeRate = 24;
        vid.Codec = VideoCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == "libx265") ?? CodecRecord.Default;
        vid.ColorPrimaries = FFColorPrimaries.BT2020;
        vid.ColorTrc = FFColorTransfer.ARIB_STD_B67;
        vid.ColorSpace = FFColorSpace.BT2020_NCL;
        vid.ColorRange = FFColorRange.MPEG;
        vid.Options.Clear();
        vid.Options.AddRange(
        [
            new AdditionalOption("preset", "medium"),
            new AdditionalOption("crf", "20"),
            new AdditionalOption("profile", "main10"),
            new AdditionalOption("x265-params",
                "repeat-headers=1"),
            // new AdditionalOption("x265-params",
            //     "colorprim=bt2020:transfer=arib-std-b67:colormatrix=bt2020nc:range=limited:repeat-headers=1"),
        ]);
        aud.Bitrate = 192000;
        aud.Codec = AudioCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == "aac") ?? CodecRecord.Default;

        _items.Add(new OutputPresetItem(
            SceneOutputExtension.Instance,
            new JsonObject
            {
                ["SelectedEncoder"] = TypeFormat.ToString(typeof(FFmpegControlledEncodingExtension)),
                ["VideoSettings"] = CoreSerializer.SerializeToJsonObject(vid),
                ["AudioSettings"] = CoreSerializer.SerializeToJsonObject(aud)
            },
            "HLG"));

        AddPlatformPresets();
    }

    private void AddPlatformPresets()
    {
        AddH264Preset(
            Strings.Preset_YouTube_1080p60,
            new PixelSize(1920, 1080),
            new Rational(60, 1),
            videoBitrate: 12_000_000,
            audioBitrate: 192_000,
            preset: "slow",
            crf: "20",
            profile: "high",
            level: "4.2");

        AddH264Preset(
            Strings.Preset_YouTube_4K60,
            new PixelSize(3840, 2160),
            new Rational(60, 1),
            videoBitrate: 45_000_000,
            audioBitrate: 192_000,
            preset: "slow",
            crf: "20",
            profile: "high",
            level: "5.1");

        AddH264Preset(
            Strings.Preset_Twitter_1080p,
            new PixelSize(1920, 1080),
            new Rational(30, 1),
            videoBitrate: 25_000_000,
            audioBitrate: 128_000,
            preset: "medium",
            crf: "21",
            profile: "high",
            level: "4.0");

        AddH264Preset(
            Strings.Preset_Instagram_Reels,
            new PixelSize(1080, 1920),
            new Rational(30, 1),
            videoBitrate: 5_000_000,
            audioBitrate: 128_000,
            preset: "medium",
            crf: "23",
            profile: "high",
            level: "4.0");

        AddH264Preset(
            Strings.Preset_Instagram_Feed,
            new PixelSize(1080, 1080),
            new Rational(30, 1),
            videoBitrate: 3_500_000,
            audioBitrate: 128_000,
            preset: "medium",
            crf: "23",
            profile: "high",
            level: "4.0");

        AddH264Preset(
            Strings.Preset_TikTok,
            new PixelSize(1080, 1920),
            new Rational(30, 1),
            videoBitrate: 6_000_000,
            audioBitrate: 128_000,
            preset: "medium",
            crf: "23",
            profile: "high",
            level: "4.0");

        AddH264Preset(
            Strings.Preset_Discord_8MB,
            new PixelSize(720, 480),
            new Rational(30, 1),
            videoBitrate: 800_000,
            audioBitrate: 64_000,
            preset: "medium",
            crf: "30",
            profile: "main",
            level: "3.1",
            audioSampleRate: 44100);
    }

    private void AddH264Preset(
        string name,
        PixelSize destinationSize,
        Rational frameRate,
        int videoBitrate,
        int audioBitrate,
        string preset,
        string crf,
        string profile,
        string level,
        int audioSampleRate = 48000)
    {
        CodecRecord videoCodec = VideoCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == "libx264") ?? CodecRecord.Default;
        CodecRecord audioCodec = AudioCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == "aac") ?? CodecRecord.Default;

        if (videoCodec == CodecRecord.Default || audioCodec == CodecRecord.Default)
        {
            return;
        }

        var vid = new FFmpegVideoEncoderSettings
        {
            Format = FFPixelFormat.YUV420P,
            Bitrate = videoBitrate,
            KeyframeRate = (int)Math.Round((double)frameRate.Numerator / Math.Max(1, frameRate.Denominator) * 2),
            Codec = videoCodec,
            SourceSize = destinationSize,
            DestinationSize = destinationSize,
            FrameRate = frameRate
        };
        vid.Options.Clear();
        vid.Options.AddRange(
        [
            new AdditionalOption("preset", preset),
            new AdditionalOption("crf", crf),
            new AdditionalOption("profile", profile),
            new AdditionalOption("level", level),
        ]);

        var aud = new FFmpegAudioEncoderSettings
        {
            Bitrate = audioBitrate,
            SampleRate = audioSampleRate,
            Codec = audioCodec
        };

        _items.Add(new OutputPresetItem(
            SceneOutputExtension.Instance,
            new JsonObject
            {
                ["SelectedEncoder"] = TypeFormat.ToString(typeof(FFmpegControlledEncodingExtension)),
                ["VideoSettings"] = CoreSerializer.SerializeToJsonObject(vid),
                ["AudioSettings"] = CoreSerializer.SerializeToJsonObject(aud)
            },
            name));
    }
}
