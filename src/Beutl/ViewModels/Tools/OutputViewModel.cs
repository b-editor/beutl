using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using System.Text.Json.Nodes;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Helpers;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using DynamicData;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Tools;

public sealed class OutputViewModel : IOutputContext, ISupportOutputPreset
{
    private readonly EditViewModel _editViewModel;
    private readonly ILogger _logger = Log.CreateLogger<OutputViewModel>();
    private readonly ReactiveProperty<bool> _isIndeterminate = new();
    private readonly ReactiveProperty<bool> _isEncoding = new();
    private readonly ReactivePropertySlim<double> _progress = new();
    private readonly ReadOnlyObservableCollection<ControllableEncodingExtension> _encoders;
    private readonly CompositeDisposable _disposable = [];
    private CancellationTokenSource? _lastCts;

    public OutputViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;
        Model = editViewModel.Scene;
        Controller = SelectedEncoder
            .CombineLatest(DestinationFile)
            .CombineWithPrevious()
            .Select(obj =>
            {
                var (newEncoder, newFile) = obj.NewValue;
                var (oldEncoder, _) = obj.OldValue;
                if (newEncoder == null || newFile == null) return null;

                if (oldEncoder == newEncoder
                    && newEncoder.IsSupported(newFile)
                    && Controller?.Value != null)
                {
                    var newController = newEncoder.CreateController(newFile);
                    var videoSettings = CoreSerializer.SerializeToJsonObject(Controller.Value.VideoSettings);
                    CoreSerializer.PopulateFromJsonObject(newController.VideoSettings, videoSettings);
                    var audioSettings = CoreSerializer.SerializeToJsonObject(Controller.Value.AudioSettings);
                    CoreSerializer.PopulateFromJsonObject(Controller.Value.AudioSettings, audioSettings);
                    return newController;
                }

                return newEncoder.CreateController(newFile);
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposable);

        VideoSettings = Controller.Select(c => c?.VideoSettings)
            .DistinctUntilChanged()
            .Select(s =>
            {
                if (s == null) return null;

                s.SourceSize = Model.FrameSize;
                s.DestinationSize = Model.FrameSize;
                return new EncoderSettingsViewModel(s);
            })
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposable);

        AudioSettings = Controller.Select(c => c?.AudioSettings)
            .DistinctUntilChanged()
            .Select(s => s == null ? null : new EncoderSettingsViewModel(s))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposable);

        CanEncode = DestinationFile.Select(x => x != null)
            .AreTrue(SelectedEncoder.Select(x => x != null))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposable);

        ExtensionProvider.Current
            .GetExtensions<ControllableEncodingExtension>()
            .AsObservableChangeSet()
            .Filter(DestinationFile.Select<string?, Func<ControllableEncodingExtension, bool>>(f => f == null
                ? _ => false
                : ext => ext.IsSupported(f)))
            .Bind(out _encoders)
            .Subscribe()
            .DisposeWith(_disposable);
    }

    public OutputExtension Extension => SceneOutputExtension.Instance;

    public Scene Model { get; }

    public CoreObject Object => Model;

    public IReactiveProperty<string> Name { get; } = new ReactiveProperty<string>("");

    public ReactivePropertySlim<string?> DestinationFile { get; } = new();

    public ReactivePropertySlim<ControllableEncodingExtension?> SelectedEncoder { get; } = new();

    public ReadOnlyObservableCollection<ControllableEncodingExtension> Encoders => _encoders;

    public ReadOnlyReactivePropertySlim<bool> CanEncode { get; }

    public ReadOnlyReactivePropertySlim<EncodingController?> Controller { get; }

    public ReadOnlyReactivePropertySlim<EncoderSettingsViewModel?> VideoSettings { get; }

    public ReadOnlyReactivePropertySlim<EncoderSettingsViewModel?> AudioSettings { get; }

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
        static string[] ToPatterns(ControllableEncodingExtension encoder)
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

        return ExtensionProvider.Current
            .GetExtensions<ControllableEncodingExtension>()
            .Select(x => new FilePickerFileType(x.Name) { Patterns = ToPatterns(x) })
            .ToArray();
    }

    public async Task StartEncode()
    {
        try
        {
            _logger.LogInformation("Starting encoding process.");
            LogEncodingSettings();
            _lastCts = new CancellationTokenSource();
            _isEncoding.Value = true;
            ProgressText.Value = "";
            Started?.Invoke(this, EventArgs.Empty);

            await Task.Run(async () =>
            {
                _isIndeterminate.Value = false;
                if (VideoSettings.Value?.Settings is not VideoEncoderSettings videoSettings
                    || AudioSettings.Value?.Settings is not AudioEncoderSettings audioSettings)
                {
                    ProgressText.Value = Message.AnUnexpectedErrorHasOccurred;
                    _logger.LogWarning("Encoder settings are null. (Encoder: {Encoder})", SelectedEncoder.Value);
                    return;
                }

                videoSettings.SourceSize = Model.FrameSize;

                ProgressMax.Value = Model.Duration.TotalSeconds * 2;

                EncodingController? controller = Controller.Value;
                if (controller == null)
                {
                    _logger.LogWarning("Encoding controller is null.");
                    return;
                }
                else
                {
                    _logger.LogInformation("Using encoding controller: {Controller}", controller);
                }

                // キャッシュ無効化
                DisableAllCache();

                // Rendererを新しく作成しない理由:
                // エンコード時のRenderInvalidatedがプレビューを更新しようとしてしまうため

                // フレームプロバイダー作成
                // using var renderer = new SceneRenderer(scene);
                var renderer = _editViewModel.Renderer.Value;
                var frameProgress = new Subject<TimeSpan>();
                var frameProvider = new FrameProviderImpl(Model, videoSettings.FrameRate, renderer, frameProgress);
                // サンプルプロバイダー作成
                using var composer = new SceneComposer(Model, renderer) { SampleRate = audioSettings.SampleRate };
                // var composer = _editViewModel.Composer.Value;
                var sampleProgress = new Subject<TimeSpan>();
                var sampleProvider = new SampleProviderImpl(
                    Model, composer, audioSettings.SampleRate, sampleProgress);

                using (frameProgress.CombineLatest(sampleProgress)
                           .Subscribe(t => ProgressValue.Value = t.Item1.TotalSeconds + t.Item2.TotalSeconds))
                {
                    await controller.Encode(frameProvider, sampleProvider, _lastCts.Token);
                }
            });

            ProgressText.Value = Strings.Completed;
            _logger.LogInformation("Encoding process completed successfully.");
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(Message.An_exception_occurred_during_output, ex.Message);
            _logger.LogError(ex, "An exception occurred during the encoding process.");
        }
        finally
        {
            EnableAllCache();
            _progress.Value = 0;
            ProgressMax.Value = 0;
            ProgressValue.Value = 0;
            _isIndeterminate.Value = false;
            _isEncoding.Value = false;
            _lastCts = null;
            Finished?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("Encoding process finished.");
        }
    }

    private static void DisableAllCache()
    {
        foreach (EditorTabItem item in EditorService.Current.TabItems)
        {
            if (item.Context.Value is EditViewModel editViewModel)
            {
                editViewModel.Renderer.Value.GetCacheContext().CacheOptions = RenderCacheOptions.Disabled;
                editViewModel.FrameCacheManager.Value.Clear();
            }
        }
    }

    private static void EnableAllCache()
    {
        foreach (EditorTabItem item in EditorService.Current.TabItems)
        {
            if (item.Context.Value is EditViewModel editViewModel)
            {
                editViewModel.Renderer.Value.GetCacheContext().CacheOptions =
                    RenderCacheOptions.CreateFromGlobalConfiguration();
            }
        }
    }

    public void CancelEncode()
    {
        _logger.LogInformation("Encoding process cancellation requested.");
        _lastCts?.Cancel();
    }

    private void LogEncodingSettings()
    {
        _logger.LogInformation("Encoding settings:");
        _logger.LogInformation("SelectedEncoder: {SelectedEncoder}", SelectedEncoder.Value?.Name);
        _logger.LogInformation("VideoSettings: {VideoSettings}",
            SerializeEncoderSettings(VideoSettings.Value?.Settings)?.ToJsonString(JsonHelper.SerializerOptions));
        _logger.LogInformation("AudioSettings: {AudioSettings}",
            SerializeEncoderSettings(AudioSettings.Value?.Settings)?.ToJsonString(JsonHelper.SerializerOptions));
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing OutputViewModel.");
        _disposable.Dispose();
        _logger.LogInformation("OutputViewModel disposed.");
    }

    private JsonObject? SerializeEncoderSettings(MediaEncoderSettings? settings)
    {
        if (settings == null) return null;
        try
        {
            return CoreSerializer.SerializeToJsonObject(settings);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "An exception occurred during serialization.");
            return null;
        }
    }

    public void WriteToJson(JsonObject json)
    {
        json[nameof(Name)] = Name.Value;
        json[nameof(DestinationFile)] = DestinationFile.Value;
        if (SelectedEncoder.Value != null)
        {
            json[nameof(SelectedEncoder)] = TypeFormat.ToString(SelectedEncoder.Value.GetType());
        }

        json[nameof(VideoSettings)] = SerializeEncoderSettings(VideoSettings.Value?.Settings);
        json[nameof(AudioSettings)] = SerializeEncoderSettings(AudioSettings.Value?.Settings);

        _logger.LogInformation("State written to JSON.");
    }

    public void ReadFromJson(JsonObject json)
    {
        ReadFromJsonCore(json, false);
    }

    private void ReadFromJsonCore(JsonObject json, bool applyingPreset)
    {
        void Deserialize(MediaEncoderSettings? settings, JsonObject json)
        {
            if (settings == null) return;
            try
            {
                CoreSerializer.PopulateFromJsonObject(settings, settings.GetType(), json);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "An exception occurred during deserialization.");
            }
        }

        if (DestinationFile.Value == null && applyingPreset)
        {
            DestinationFile.Value = Path.Combine(
                Path.GetDirectoryName(Model.Uri!.LocalPath)!,
                $"{Path.GetFileNameWithoutExtension(Model.Uri!.LocalPath)}.mp4");
        }

        if (!applyingPreset)
        {
            if (json.TryGetPropertyValue(nameof(DestinationFile), out JsonNode? dstFileNode)
                && dstFileNode is JsonValue dstFileValue
                && dstFileValue.TryGetValue(out string? dstFile))
            {
                DestinationFile.Value = dstFile;
            }

            if (json.TryGetPropertyValue(nameof(Name), out JsonNode? nameNode)
                && nameNode is JsonValue nameValue
                && nameValue.TryGetValue(out string? name))
            {
                Name.Value = name;
            }
        }

        if (json.TryGetPropertyValue(nameof(SelectedEncoder), out JsonNode? encoderNode)
            && encoderNode is JsonValue encoderValue
            && encoderValue.TryGetValue(out string? encoderStr)
            && TypeFormat.ToType(encoderStr) is { } encoderType
            && ExtensionProvider.Current.GetExtensions<ControllableEncodingExtension>()
                .FirstOrDefault(x => x.GetType() == encoderType) is { } encoder)
        {
            SelectedEncoder.Value = encoder;
        }

        // 上のSelectedEncoder.Value = encoder;でnull以外が指定された場合、VideoSettings, AudioSettingsもnullじゃなくなる。
        if (json.TryGetPropertyValue(nameof(VideoSettings), out JsonNode? videoNode)
            && videoNode is JsonObject videoObj
            && VideoSettings.Value?.Settings is VideoEncoderSettings videoSettings)
        {
            PixelSize srcSize = default;
            PixelSize dstSize = default;
            Rational framerate = default;
            if (applyingPreset)
            {
                srcSize = videoSettings.SourceSize;
                dstSize = videoSettings.DestinationSize;
                framerate = videoSettings.FrameRate;
            }

            Deserialize(videoSettings, videoObj);
            if (applyingPreset)
            {
                videoSettings.SourceSize = srcSize;
                videoSettings.DestinationSize = dstSize;
                videoSettings.FrameRate = framerate;
            }
        }

        if (json.TryGetPropertyValue(nameof(AudioSettings), out JsonNode? audioNode)
            && audioNode is JsonObject audioObj
            && AudioSettings.Value?.Settings is AudioEncoderSettings audioSettings)
        {
            int sampleRate = 0;
            if (applyingPreset)
            {
                sampleRate = audioSettings.SampleRate;
            }

            Deserialize(AudioSettings.Value?.Settings, audioObj);
            if (applyingPreset)
            {
                audioSettings.SampleRate = sampleRate;
            }
        }

        _logger.LogInformation("State read from JSON.");
    }

    public void Apply(JsonObject preset)
    {
        ReadFromJsonCore(preset, true);
    }

    public JsonObject ToPreset()
    {
        var obj = new JsonObject();
        WriteToJson(obj);
        return obj;
    }
}
