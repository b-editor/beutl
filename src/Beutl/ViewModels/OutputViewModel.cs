using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

using Avalonia.Platform.Storage;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.Rendering;
using Beutl.Rendering.Cache;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using DynamicData;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace Beutl.ViewModels;

public sealed class OutputViewModel : IOutputContext
{
    private readonly ILogger _logger = Log.CreateLogger<OutputViewModel>();
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

        SelectedEncoder.Subscribe(obj =>
        {
            if (obj != null)
            {
                var settings = obj.DefaultVideoConfig();
                settings.SourceSize = new(Model.Width, Model.Height);
                settings.DestinationSize = new(Model.Width, Model.Height);
                VideoSettings.Value = new EncoderSettingsViewModel(settings);
            }
            else
            {
                VideoSettings.Value = null;
            }

            if (obj != null)
            {
                AudioSettings.Value = new EncoderSettingsViewModel(obj.DefaultAudioConfig());
            }
            else
            {
                AudioSettings.Value = null;
            }
        });

        CanEncode = DestinationFile.Select(x => x != null)
            .AreTrue(SelectedEncoder.Select(x => x != null))
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

    public ReactiveProperty<EncoderSettingsViewModel?> VideoSettings { get; } = new();

    public ReactiveProperty<EncoderSettingsViewModel?> AudioSettings { get; } = new();

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
                    if (VideoSettings.Value?.Settings is not VideoEncoderSettings videoSettings
                        || AudioSettings.Value?.Settings is not AudioEncoderSettings audioSettings)
                    {
                        ProgressText.Value = Message.AnUnexpectedErrorHasOccurred;
                        _logger.LogWarning("EncoderSettings is null. ({Encoder})", SelectedEncoder.Value);
                        return;
                    }
                    videoSettings.SourceSize = scene.FrameSize;

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
                        using var renderer = new SceneRenderer(scene);
                        OutputVideo(frames, frameRateD, renderer, writer);

                        using var composer = new SceneComposer(scene, renderer);
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
            NotificationService.ShowError(Message.An_exception_occurred_during_output, ex.Message);
            _logger.LogError(ex, "An exception occurred during output.");
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

    private void OutputVideo(double frames, double frameRate, SceneRenderer renderer, MediaWriter writer)
    {
        RenderCacheContext? cacheContext = renderer.GetCacheContext();

        if (cacheContext != null)
        {
            cacheContext.CacheOptions = RenderCacheOptions.Disabled;
        }

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

    private void OutputAudio(double samples, SceneComposer composer, MediaWriter writer)
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
        JsonObject? Serialize(MediaEncoderSettings? settings)
        {
            if (settings == null) return null;
            try
            {
                return CoreSerializerHelper.SerializeToJsonObject(settings, settings.GetType());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "An exception occurred during serialize.");
                return null;
            }
        }

        json[nameof(DestinationFile)] = DestinationFile.Value;
        if (SelectedEncoder.Value != null)
        {
            json[nameof(SelectedEncoder)] = TypeFormat.ToString(SelectedEncoder.Value.GetType());
        }

        json[nameof(IsEncodersExpanded)] = IsEncodersExpanded.Value;
        json[nameof(ScrollOffset)] = ScrollOffset.Value.ToString();
        json[nameof(VideoSettings)] = Serialize(VideoSettings.Value?.Settings);
        json[nameof(AudioSettings)] = Serialize(AudioSettings.Value?.Settings);
    }

    public void ReadFromJson(JsonObject json)
    {
        void Deserialize(MediaEncoderSettings? settings, JsonObject json)
        {
            if (settings == null) return;
            try
            {
                CoreSerializerHelper.PopulateFromJsonObject(settings, settings.GetType(), json);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "An exception occurred during deserialize.");
            }
        }

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

        // 上のSelectedEncoder.Value = encoder;でnull以外が指定された場合、VideoSettings, AudioSettingsもnullじゃなくなる。
        if (json.TryGetPropertyValue(nameof(VideoSettings), out JsonNode? videoNode)
            && videoNode is JsonObject videoObj)
        {
            Deserialize(VideoSettings.Value?.Settings, videoObj);
        }

        if (json.TryGetPropertyValue(nameof(AudioSettings), out JsonNode? audioNode)
            && audioNode is JsonObject audioObj)
        {
            Deserialize(AudioSettings.Value?.Settings, audioObj);
        }
    }
}
