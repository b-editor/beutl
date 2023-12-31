using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

using Avalonia.Platform.Storage;

using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.Rendering;
using Beutl.Rendering.Cache;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.Utilities;

using DynamicData;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

using Serilog;

namespace Beutl.ViewModels;

public sealed class VideoOutputViewModel
{
    private double _aspectRatio;
    private bool _changingSize;

    public VideoOutputViewModel(int width, int height, Rational frameRate, int bitRate, int keyframeRate)
    {
        Width.Value = width;
        Height.Value = height;
        InputFrameRate.Value = frameRate.ToString();
        BitRate.Value = bitRate;
        KeyFrameRate.Value = keyframeRate;
        FrameRate = new ReactivePropertySlim<Rational>(frameRate);

        FixAspectRatio.Where(x => x)
            .Subscribe(_ => _aspectRatio = Width.Value / (double)Height.Value);

        Width.Skip(1)
            .Where(_ => FixAspectRatio.Value && !_changingSize)
            .Subscribe(w =>
            {
                try
                {
                    _changingSize = true;
                    Height.Value = (int)(w / _aspectRatio);
                }
                finally
                {
                    _changingSize = false;
                }
            });
        Height.Skip(1)
            .Where(_ => FixAspectRatio.Value && !_changingSize)
            .Subscribe(h =>
            {
                try
                {
                    _changingSize = true;
                    Width.Value = (int)(h * _aspectRatio);
                }
                finally
                {
                    _changingSize = false;
                }
            });

        Width.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        Height.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        BitRate.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        KeyFrameRate.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        InputFrameRate.SetValidateNotifyError(x =>
        {
            if (Rational.TryParse(x, null, out Rational rate))
            {
                if (MathUtilities.LessThanOrClose(rate.ToDouble(), 0))
                {
                    return Message.ValueLessThanOrEqualToZero;
                }
                else
                {
                    FrameRate.Value = rate;
                    return null;
                }
            }
            else
            {
                return Message.InvalidString;
            }
        });
        OptionsString.SetValidateNotifyError(x =>
        {
            try
            {
                if (x == null || JsonNode.Parse(x) is not { } json)
                {
                    return Message.InvalidString;
                }
                else
                {
                    OptionsJson.Value = json;
                    return null;
                }
            }
            catch
            {
                return Message.InvalidString;
            }
        });

        IsValid = Width.ObserveHasErrors
            .AnyTrue(Height.ObserveHasErrors,
                     BitRate.ObserveHasErrors,
                     KeyFrameRate.ObserveHasErrors,
                     InputFrameRate.ObserveHasErrors)
            .Not()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<int> Width { get; } = new();

    public ReactiveProperty<int> Height { get; } = new();

    public ReactiveProperty<bool> FixAspectRatio { get; } = new(true);

    public ReactivePropertySlim<bool> IsFrameSizeExpanded { get; } = new(true);

    public ReactiveProperty<string> InputFrameRate { get; } = new();

    public ReactivePropertySlim<Rational> FrameRate { get; }

    public ReactiveProperty<int> BitRate { get; } = new();

    public ReactiveProperty<int> KeyFrameRate { get; } = new();

    public ReactiveProperty<string?> OptionsString { get; } = new();

    public ReactivePropertySlim<JsonNode?> OptionsJson { get; } = new();

    public ReactivePropertySlim<bool> IsOptionsExpanded { get; } = new(false);

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public VideoEncoderSettings ToSettings(PixelSize sourceSize)
    {
        return new VideoEncoderSettings(
            sourceSize,
            new PixelSize(Width.Value, Height.Value),
            FrameRate.Value,
            BitRate.Value,
            KeyFrameRate.Value)
        {
            CodecOptions = OptionsJson.Value
        };
    }

    private static string? ValidateLessThanOrEqualToZero(int value)
    {
        if (value <= 0)
            return Message.ValueLessThanOrEqualToZero;
        else
            return null;
    }

    public JsonNode WriteToJson()
    {
        return new JsonObject
        {
            [nameof(Width)] = Width.Value,
            [nameof(Height)] = Height.Value,
            [nameof(FixAspectRatio)] = FixAspectRatio.Value,
            [nameof(FrameRate)] = InputFrameRate.Value,
            [nameof(BitRate)] = BitRate.Value,
            [nameof(KeyFrameRate)] = KeyFrameRate.Value,
            ["Options"] = OptionsJson.Value?.DeepClone(),
            [nameof(IsOptionsExpanded)] = IsOptionsExpanded.Value,
        };
    }

    public void ReadFromJson(JsonNode json)
    {
        try
        {
            _changingSize = true;
            JsonObject obj = json.AsObject();
            Width.Value = obj[nameof(Width)]!.AsValue().GetValue<int>();
            Height.Value = obj[nameof(Height)]!.AsValue().GetValue<int>();
            FixAspectRatio.Value = obj[nameof(FixAspectRatio)]!.AsValue().GetValue<bool>();
            InputFrameRate.Value = obj[nameof(FrameRate)]!.AsValue().GetValue<string>();
            BitRate.Value = obj[nameof(BitRate)]!.AsValue().GetValue<int>();
            KeyFrameRate.Value = obj[nameof(KeyFrameRate)]!.AsValue().GetValue<int>();
            OptionsJson.Value = obj["Options"];
            if (OptionsJson.Value != null)
            {
                OptionsString.Value = OptionsJson.Value.ToJsonString(JsonHelper.SerializerOptions);
            }

            IsOptionsExpanded.Value = obj[nameof(IsOptionsExpanded)]!.AsValue().GetValue<bool>();
        }
        catch
        {
        }
        finally
        {
            _changingSize = false;
        }
    }
}

public sealed class AudioOutputViewModel
{
    public AudioOutputViewModel(int sampleRate, int channels, int bitrate)
    {
        SampleRate.Value = sampleRate;
        BitRate.Value = bitrate;
        Channels.Value = channels;

        SampleRate.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        BitRate.SetValidateNotifyError(ValidateLessThanOrEqualToZero);
        Channels.SetValidateNotifyError(x => x is >= 1 and <= 2 ? null : Message.Invalid_choice);
        OptionsString.SetValidateNotifyError(x =>
        {
            try
            {
                if (x == null || JsonNode.Parse(x) is not { } json)
                {
                    return Message.InvalidString;
                }
                else
                {
                    OptionsJson.Value = json;
                    return null;
                }
            }
            catch
            {
                return Message.InvalidString;
            }
        });

        IsValid = SampleRate.ObserveHasErrors
            .AnyTrue(Channels.ObserveHasErrors, BitRate.ObserveHasErrors)
            .Not()
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<int> SampleRate { get; } = new();

    public ReactiveProperty<int> Channels { get; } = new();

    public ReactiveProperty<int> BitRate { get; } = new();

    public ReactiveProperty<string?> OptionsString { get; } = new();

    public ReactivePropertySlim<JsonNode?> OptionsJson { get; } = new();

    public ReactivePropertySlim<bool> IsOptionsExpanded { get; } = new(false);

    public ReadOnlyReactivePropertySlim<bool> IsValid { get; }

    public AudioEncoderSettings ToSettings()
    {
        return new AudioEncoderSettings(
            SampleRate.Value,
            Channels.Value + 1,
            BitRate.Value)
        {
            CodecOptions = OptionsJson.Value
        };
    }

    private static string? ValidateLessThanOrEqualToZero(int value)
    {
        if (value <= 0)
            return Message.ValueLessThanOrEqualToZero;
        else
            return null;
    }

    public JsonNode WriteToJson()
    {
        return new JsonObject
        {
            [nameof(SampleRate)] = SampleRate.Value,
            [nameof(Channels)] = Channels.Value,
            [nameof(BitRate)] = BitRate.Value,
            ["Options"] = OptionsJson.Value?.DeepClone(),
            [nameof(IsOptionsExpanded)] = IsOptionsExpanded.Value,
        };
    }

    public void ReadFromJson(JsonNode json)
    {

    }
}

public sealed class OutputViewModel : IOutputContext
{
    private readonly ILogger _logger = Log.ForContext<OutputViewModel>();
    private readonly ReactiveProperty<bool> _isIndeterminate = new();
    private readonly ReactiveProperty<bool> _isEncoding = new();
    private readonly ReactivePropertySlim<double> _progress = new();
    private readonly ReadOnlyObservableCollection<IEncoderInfo> _encoders;
    private readonly IDisposable _disposable1;
    private readonly ProjectItemContainer _itemContainer = ProjectItemContainer.Current;
    private CancellationTokenSource? _lastCts;

    public OutputViewModel(SceneFile model)
    {
        Model = model;

        const int framerate = 30;
        const int samplerate = 44100;

        VideoSettings = new VideoOutputViewModel(
            width: model.Width,
            height: model.Height,
            frameRate: new Rational(framerate),
            bitRate: 5_000_000,
            keyframeRate: 12);
        AudioSettings = new AudioOutputViewModel(samplerate, 1, 128_000);

        SelectedEncoder.Subscribe(obj =>
        {
            if (obj?.DefaultVideoConfig()?.CodecOptions is { } videoCodecOptions)
            {
                VideoSettings.OptionsString.Value = videoCodecOptions.ToJsonString(JsonHelper.SerializerOptions);
            }

            if (obj?.DefaultAudioConfig()?.CodecOptions is { } audioCodecOptions)
            {
                AudioSettings.OptionsString.Value = audioCodecOptions.ToJsonString(JsonHelper.SerializerOptions);
            }
        });

        CanEncode = VideoSettings.IsValid
            .AreTrue(AudioSettings.IsValid,
                     DestinationFile.Select(x => x != null),
                     SelectedEncoder.Select(x => x != null))
            .ToReadOnlyReactivePropertySlim();

        _disposable1 = EncoderRegistry.EnumerateEncoders().AsObservableChangeSet()
            .Filter(DestinationFile.Select<string?, Func<IEncoderInfo, bool>>(
                f => f == null
                    ? _ => false
                    : ext => ext.IsSupported(f)))
            .Bind(out _encoders)
            .Subscribe();
    }

    public OutputExtension Extension => SceneOutputExtension.Instance;

    public SceneFile Model { get; }

    public string TargetFile => Model.FileName;

    public ReactivePropertySlim<string?> DestinationFile { get; } = new();

    public ReactivePropertySlim<IEncoderInfo?> SelectedEncoder { get; } = new();

    public ReadOnlyObservableCollection<IEncoderInfo> Encoders => _encoders;

    public ReactivePropertySlim<bool> IsEncodersExpanded { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanEncode { get; }

    public VideoOutputViewModel VideoSettings { get; }

    public AudioOutputViewModel AudioSettings { get; }

    public ReactivePropertySlim<Avalonia.Vector> ScrollOffset { get; } = new();

    public ReactiveProperty<double> ProgressMax { get; } = new();

    public ReactiveProperty<double> ProgressValue { get; } = new();

    public ReactiveProperty<string> ProgressText { get; } = new();

    public IReadOnlyReactiveProperty<bool> IsIndeterminate => _isIndeterminate;

    public IReadOnlyReactiveProperty<bool> IsEncoding => _isEncoding;

    IReadOnlyReactiveProperty<double> IOutputContext.Progress => _progress;

    public event EventHandler? Started;

    public event EventHandler? Finished;

    public static FilePickerFileType[] GetFilePickerFileTypes()
    {
        static string[] ToPatterns(IEncoderInfo encoder)
        {
            return encoder.SupportExtensions()
                .Select(x =>
                {
                    if (x.Contains('*', StringComparison.Ordinal))
                    {
                        return x;
                    }
                    else
                    {
                        if (x.StartsWith('.'))
                        {
                            return $"*{x}";
                        }
                        else
                        {
                            return $"*.{x}";
                        }
                    }
                })
                .ToArray();
        }

        return EncoderRegistry.EnumerateEncoders()
            .Select(x => new FilePickerFileType(x.Name) { Patterns = ToPatterns(x) })
            .ToArray();
    }

    public async void StartEncode()
    {
        try
        {
            _lastCts = new CancellationTokenSource();
            _isEncoding.Value = true;
            Started?.Invoke(this, EventArgs.Empty);

            await RenderThread.Dispatcher.InvokeAsync(() =>
            {
                _isIndeterminate.Value = true;
                if (!_itemContainer.TryGetOrCreateItem(TargetFile, out Scene? scene))
                {
                    // シーンの読み込みに失敗。
                    ProgressText.Value = Message.Could_not_load_scene;
                }
                else
                {
                    _isIndeterminate.Value = false;
                    VideoEncoderSettings videoSettings = VideoSettings.ToSettings(new PixelSize(scene.Width, scene.Height));
                    AudioEncoderSettings audioSettings = AudioSettings.ToSettings();

                    TimeSpan duration = scene.Duration;
                    Rational frameRate = videoSettings.FrameRate;
                    double frameRateD = frameRate.ToDouble();
                    double frames = duration.TotalSeconds * frameRateD;
                    double samples = duration.TotalSeconds;
                    ProgressMax.Value = frames + samples;

                    MediaWriter? writer = SelectedEncoder.Value!.Create(DestinationFile.Value!, videoSettings, audioSettings);
                    if (writer == null) return;

                    try
                    {
                        IRenderer renderer = scene.Renderer;
                        OutputVideo(frames, frameRateD, renderer, writer);

                        IComposer composer = scene.Composer;
                        OutputAudio(samples, composer, writer);
                    }
                    finally
                    {
                        _isIndeterminate.Value = true;
                        writer.Dispose();
                        _isIndeterminate.Value = false;
                    }
                }
            });

            ProgressText.Value = Strings.Completed;
        }
        catch (Exception ex)
        {
            Telemetry.Exception(ex);
            NotificationService.ShowError(Message.An_exception_occurred_during_output, ex.Message);
            _logger.Error(ex, "An exception occurred during output.");
        }
        finally
        {
            _progress.Value = 0;
            ProgressMax.Value = 0;
            ProgressValue.Value = 0;
            _isIndeterminate.Value = false;
            _isEncoding.Value = false;
            _lastCts = null;
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OutputVideo(double frames, double frameRate, IRenderer renderer, MediaWriter writer)
    {
        RenderCacheContext? cacheContext = renderer.GetCacheContext();
        RenderCacheOptions? restoreCacheOptions = null;

        if (cacheContext != null)
        {
            restoreCacheOptions = cacheContext.CacheOptions;
            cacheContext.CacheOptions = RenderCacheOptions.Disabled;
        }
        try
        {
            for (double i = 0; i < frames; i++)
            {
                if (_lastCts!.IsCancellationRequested)
                    break;

                var ts = TimeSpan.FromSeconds(i / frameRate);
                int retry = 0;
            Retry:
                if (!renderer.Render(ts))
                {
                    if (retry > 3)
                        throw new Exception("Renderer.RenderがFalseでした。他にこのシーンを使用していないか確認してください。");

                    retry++;
                    goto Retry;
                }
                using (Bitmap<Bgra8888> result = renderer.Snapshot())
                {
                    writer.AddVideo(result);
                }

                ProgressValue.Value++;
                _progress.Value = ProgressValue.Value / ProgressMax.Value;
                ProgressText.Value = $"{Strings.OutputtingVideo}: {ts:hh\\:mm\\:ss\\.ff}";
            }
        }
        finally
        {
            if (cacheContext != null && restoreCacheOptions != null)
            {
                cacheContext.CacheOptions = restoreCacheOptions;
            }
        }
    }

    private void OutputAudio(double samples, IComposer composer, MediaWriter writer)
    {
        for (double i = 0; i < samples; i++)
        {
            if (_lastCts!.IsCancellationRequested)
                break;

            var ts = TimeSpan.FromSeconds(i);

            using (Pcm<Stereo32BitFloat> result = composer.Compose(ts)!)
            {
                writer.AddAudio(result);
            }

            ProgressValue.Value++;
            _progress.Value = ProgressValue.Value / ProgressMax.Value;
            ProgressText.Value = $"{Strings.OutputtingAudio}: {ts:hh\\:mm\\:ss\\.ff}";
        }
    }

    public void CancelEncode()
    {
        _lastCts?.Cancel();
    }

    public void Dispose()
    {
        _disposable1.Dispose();
    }

    public void WriteToJson(JsonObject json)
    {
        json[nameof(DestinationFile)] = DestinationFile.Value;
        if (SelectedEncoder.Value != null)
        {
            json[nameof(SelectedEncoder)] = TypeFormat.ToString(SelectedEncoder.Value.GetType());
        }

        json[nameof(IsEncodersExpanded)] = IsEncodersExpanded.Value;
        json[nameof(ScrollOffset)] = ScrollOffset.Value.ToString();
        json[nameof(VideoSettings)] = VideoSettings.WriteToJson();
        json[nameof(AudioSettings)] = AudioSettings.WriteToJson();
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue(nameof(DestinationFile), out JsonNode? dstFileNode)
            && dstFileNode is JsonValue dstFileValue
            && dstFileValue.TryGetValue(out string? dstFile))
        {
            DestinationFile.Value = dstFile;
        }

        if (json.TryGetPropertyValue(nameof(SelectedEncoder), out JsonNode? encoderNode)
            && encoderNode is JsonValue encoderValue
            && encoderValue.TryGetValue(out string? encoderStr)
            && TypeFormat.ToType(encoderStr) is Type encoderType
            && EncoderRegistry.EnumerateEncoders().FirstOrDefault(x => x.GetType() == encoderType) is { } encoder)
        {
            SelectedEncoder.Value = encoder;
        }

        if (json.TryGetPropertyValue(nameof(IsEncodersExpanded), out JsonNode? isExpandedNode)
            && isExpandedNode is JsonValue isExpandedValue
            && isExpandedValue.TryGetValue(out bool isExpanded))
        {
            IsEncodersExpanded.Value = isExpanded;
        }

        if (json.TryGetPropertyValue(nameof(ScrollOffset), out JsonNode? scrollOfstNode)
            && scrollOfstNode is JsonValue scrollOfstValue
            && scrollOfstValue.TryGetValue(out string? scrollOfstStr)
            && Graphics.Vector.TryParse(scrollOfstStr, out Graphics.Vector vec))
        {
            ScrollOffset.Value = new Avalonia.Vector(vec.X, vec.Y);
        }

        if (json.TryGetPropertyValue(nameof(VideoSettings), out JsonNode? videoNode)
            && videoNode is JsonObject videoObj)
        {
            VideoSettings.ReadFromJson(videoObj);
        }

        if (json.TryGetPropertyValue(nameof(AudioSettings), out JsonNode? audioNode)
            && audioNode is JsonObject audioObj)
        {
            AudioSettings.ReadFromJson(audioObj);
        }
    }
}
