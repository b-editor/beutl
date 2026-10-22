using System.Text.Json.Nodes;
using Beutl.Configuration;
using Beutl.Editor.Components.OutputTab;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.Helpers;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Tools;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public sealed class OutputPresetService : IOutputPresetStore
{
    // Set once the saved presets were matched to the platform presets by name, which only those saved before PresetKey need.
    internal const string PlatformPresetNamesMatchedKey = "OutputPresetService.PlatformPresetNamesMatched";

    public static readonly OutputPresetService Instance = new();
    private readonly CoreList<OutputPresetItem> _items = [];

    private readonly string _filePath;
    private readonly IPreferences _preferences;

    private readonly ILogger _logger = Log.CreateLogger<OutputPresetService>();
    private bool _isRestored;
    private bool _matchesPlatformPresetNames;

    private OutputPresetService()
        : this(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "output-presets.json"), Preferences.Default)
    {
    }

    // Tests restore presets and preferences of their own.
    internal OutputPresetService(string filePath, IPreferences preferences)
    {
        _filePath = filePath;
        _preferences = preferences;
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

        array.JsonSave(_filePath);
        _logger.LogInformation("Saved {Count} OutputPresetItem to file: {FilePath}", _items.Count, _filePath);
    }

    public void RestoreItems()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogWarning("Output preset file not found: {FilePath}", _filePath);
                _isRestored = true;
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

            // 既存ファイルの読み込みに成功した場合のみ書き込みを許可する。
            // try 冒頭で true にすると、IO 失敗や JSON 破損時に finally の
            // CreateDefaultPresets/AddPlatformPresets 経由で SaveItems が走り、
            // ユーザーの保存済みプリセットがデフォルトで上書きされてしまう。
            _isRestored = true;
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
            else
            {
                AddPlatformPresets();
            }
        }
    }

    private void CreateDefaultPresets()
    {
        var vid = new FFmpegVideoEncoderSettings();
        vid.Format = FFPixelFormat.YUV420P;
        vid.Bitrate = 15000000;
        vid.KeyframeRate = 12;
        vid.Codec = FindVideoCodec("libx264");
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
        aud.Codec = FindAudioCodec("aac");

        _items.Add(CreateFFmpegPreset(vid, aud, "High Quality"));

        ObjectRegenerator.Regenerate<FFmpegVideoEncoderSettings>(vid, out vid);
        ObjectRegenerator.Regenerate<FFmpegAudioEncoderSettings>(aud, out aud);

        vid.Bitrate = 8000000;
        vid.KeyframeRate = 30;
        vid.Options.First(i => i.Name == "preset").Value = "medium";
        vid.Options.First(i => i.Name == "crf").Value = "23";
        aud.Bitrate = 128000;

        _items.Add(CreateFFmpegPreset(vid, aud, "Medium Quality"));

        ObjectRegenerator.Regenerate<FFmpegVideoEncoderSettings>(vid, out vid);
        ObjectRegenerator.Regenerate<FFmpegAudioEncoderSettings>(aud, out aud);

        vid.Bitrate = 3000000;
        vid.KeyframeRate = 60;
        vid.Options.First(i => i.Name == "preset").Value = "ultrafast";
        vid.Options.First(i => i.Name == "crf").Value = "28";
        aud.Bitrate = 128000;

        _items.Add(CreateFFmpegPreset(vid, aud, "Low Quality"));

        // HDR10 (PQ)
        ObjectRegenerator.Regenerate<FFmpegVideoEncoderSettings>(vid, out vid);
        ObjectRegenerator.Regenerate<FFmpegAudioEncoderSettings>(aud, out aud);

        ConfigureHdr(
            vid,
            aud,
            FFColorTransfer.SMPTE2084,
            "colorprim=bt2020:transfer=smpte2084:colormatrix=bt2020nc:range=limited"
            + ":master-display=G(13250,34500)B(7500,3000)R(34000,16000)WP(15635,16450)L(10000000,1):max-cll=1000,400:repeat-headers=1");

        _items.Add(CreateFFmpegPreset(vid, aud, "HDR10 (PQ)"));

        // HLG
        ObjectRegenerator.Regenerate<FFmpegVideoEncoderSettings>(vid, out vid);
        ObjectRegenerator.Regenerate<FFmpegAudioEncoderSettings>(aud, out aud);

        ConfigureHdr(
            vid,
            aud,
            FFColorTransfer.ARIB_STD_B67,
            "repeat-headers=1");

        _items.Add(CreateFFmpegPreset(vid, aud, "HLG"));

        AddPlatformPresets();
    }

    // The HDR presets differ only in the transfer curve and what x265 is told about it.
    private static void ConfigureHdr(
        FFmpegVideoEncoderSettings vid,
        FFmpegAudioEncoderSettings aud,
        FFColorTransfer transfer,
        string x265Params)
    {
        vid.Format = FFPixelFormat.YUV420P10LE;
        vid.Bitrate = 20000000;
        vid.KeyframeRate = 24;
        vid.Codec = FindVideoCodec("libx265");
        vid.ColorPrimaries = FFColorPrimaries.BT2020;
        vid.ColorTrc = transfer;
        vid.ColorSpace = FFColorSpace.BT2020_NCL;
        vid.ColorRange = FFColorRange.MPEG;
        vid.Options.Clear();
        vid.Options.AddRange(
        [
            new AdditionalOption("preset", "medium"),
            new AdditionalOption("crf", "20"),
            new AdditionalOption("profile", "main10"),
            new AdditionalOption("x265-params", x265Params),
        ]);
        aud.Bitrate = 192000;
        aud.Codec = FindAudioCodec("aac");
    }

    private void AddPlatformPresets()
    {
        _matchesPlatformPresetNames = !_preferences.Get(PlatformPresetNamesMatchedKey, false);
        bool changed = false;

        changed |= AddH264Preset(
            presetKey: "Platform.YouTube_1080p60",
            name: Strings.Preset_YouTube_1080p60,
            destinationSize: new PixelSize(1920, 1080),
            frameRate: new Rational(60, 1),
            videoBitrate: 12_000_000,
            audioBitrate: 192_000,
            preset: "slow",
            crf: "20",
            profile: "high",
            level: "4.2");

        changed |= AddH264Preset(
            presetKey: "Platform.YouTube_4K60",
            name: Strings.Preset_YouTube_4K60,
            destinationSize: new PixelSize(3840, 2160),
            frameRate: new Rational(60, 1),
            videoBitrate: 45_000_000,
            audioBitrate: 192_000,
            preset: "slow",
            crf: "20",
            profile: "high",
            level: "5.1");

        changed |= AddH264Preset(
            presetKey: "Platform.Twitter_1080p",
            name: Strings.Preset_Twitter_1080p,
            destinationSize: new PixelSize(1920, 1080),
            frameRate: new Rational(30, 1),
            videoBitrate: 25_000_000,
            audioBitrate: 128_000,
            preset: "medium",
            crf: "21",
            profile: "high",
            level: "4.0");

        changed |= AddH264Preset(
            presetKey: "Platform.Instagram_Reels",
            name: Strings.Preset_Instagram_Reels,
            destinationSize: new PixelSize(1080, 1920),
            frameRate: new Rational(30, 1),
            videoBitrate: 5_000_000,
            audioBitrate: 128_000,
            preset: "medium",
            crf: "23",
            profile: "high",
            level: "4.0");

        changed |= AddH264Preset(
            presetKey: "Platform.Instagram_Feed",
            name: Strings.Preset_Instagram_Feed,
            destinationSize: new PixelSize(1080, 1080),
            frameRate: new Rational(30, 1),
            videoBitrate: 3_500_000,
            audioBitrate: 128_000,
            preset: "medium",
            crf: "23",
            profile: "high",
            level: "4.0");

        changed |= AddH264Preset(
            presetKey: "Platform.TikTok",
            name: Strings.Preset_TikTok,
            destinationSize: new PixelSize(1080, 1920),
            frameRate: new Rational(30, 1),
            videoBitrate: 6_000_000,
            audioBitrate: 128_000,
            preset: "medium",
            crf: "23",
            profile: "high",
            level: "4.0");

        changed |= AddH264Preset(
            presetKey: "Platform.Discord_8MB",
            name: Strings.Preset_Discord_8MB,
            destinationSize: new PixelSize(720, 480),
            frameRate: new Rational(30, 1),
            videoBitrate: 800_000,
            audioBitrate: 64_000,
            preset: "medium",
            crf: "30",
            profile: "main",
            level: "3.1",
            audioSampleRate: 44100);

        if (changed)
        {
            // バックフィル / 新規追加を即時永続化しないと、ユーザーが何も操作せず
            // 終了した後にロケールを切り替えて再起動した場合、前回のマイグレーションが
            // 失われて重複登録が再発する。
            try
            {
                SaveItems();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An exception has occurred while saving output presets after platform preset migration.");
                return;
            }
        }

        if (_isRestored && _matchesPlatformPresetNames)
        {
            try
            {
                _preferences.Set(PlatformPresetNamesMatchedKey, true);
            }
            catch (Exception ex)
            {
                // Failing here only means the next start matches the names again.
                _logger.LogError(ex, "An exception has occurred while recording that the platform preset names were matched.");
            }
        }
    }

    private bool AddH264Preset(
        string presetKey,
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
        // 既知のプラットフォームプリセットは PresetKey で照合し、ロケール変更で
        // ローカライズ名が変わっても重複追加されないようにする。PresetKey 未対応の
        // 保存データから復元したアイテムは PresetKey が null なので、最初の一度だけ
        // 名前一致で後方互換照合した上でキーをバックフィルする。
        OutputPresetItem? existing = _items.FirstOrDefault(
            i => i.PresetKey == presetKey
                 || (_matchesPlatformPresetNames && i.PresetKey == null && i.Name.Value == name));

        if (existing != null)
        {
            return UpdateSavedPlatformPreset(existing, presetKey);
        }

        CodecRecord videoCodec = FindVideoCodec("libx264");
        CodecRecord audioCodec = FindAudioCodec("aac");

        if (videoCodec == CodecRecord.Default || audioCodec == CodecRecord.Default)
        {
            return false;
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

        OutputPresetItem item = CreateFFmpegPreset(vid, aud, name, presetKey);
        MarkAsApplyingFrameSizeAndRates(item.Json);
        _items.Add(item);

        return true;
    }

    /// <summary>Brings a platform preset restored from the saved presets up to date, reporting whether it changed.</summary>
    internal static bool UpdateSavedPlatformPreset(OutputPresetItem existing, string presetKey)
    {
        bool changed = false;
        if (existing.PresetKey == null)
        {
            existing.PresetKey = presetKey;
            changed = true;
        }

        // A platform preset saved before presets could apply their frame size and rates still promises them by name,
        // so it gets the key that makes it apply them.
        changed |= MarkAsApplyingFrameSizeAndRates(existing.Json);
        return changed;
    }

    /// <summary>Makes a preset apply its frame size and rates, reporting whether it did not already.</summary>
    private static bool MarkAsApplyingFrameSizeAndRates(JsonObject preset)
    {
        if (preset.TryGetPropertyValueAsJsonValue(OutputViewModel.AppliesFrameSizeAndRatesKey, out bool applies)
            && applies)
        {
            return false;
        }

        preset[OutputViewModel.AppliesFrameSizeAndRatesKey] = true;
        return true;
    }

    private static OutputPresetItem CreateFFmpegPreset(
        FFmpegVideoEncoderSettings vid,
        FFmpegAudioEncoderSettings aud,
        string name,
        string? presetKey = null)
    {
        return new OutputPresetItem(
            SceneOutputExtension.Instance,
            new JsonObject
            {
                ["SelectedEncoder"] = TypeFormat.ToString(typeof(FFmpegControlledEncodingExtension)),
                ["VideoSettings"] = CoreSerializer.SerializeToJsonObject(vid),
                ["AudioSettings"] = CoreSerializer.SerializeToJsonObject(aud)
            },
            name,
            presetKey);
    }

    private static CodecRecord FindVideoCodec(string name)
    {
        return VideoCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == name) ?? CodecRecord.Default;
    }

    private static CodecRecord FindAudioCodec(string name)
    {
        return AudioCodecChoicesProvider.GetChoices()
            .Cast<CodecRecord>()
            .FirstOrDefault(i => i.Name == name) ?? CodecRecord.Default;
    }
}
