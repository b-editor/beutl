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
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using DynamicData;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Tools;

public sealed class OutputViewModel : IOutputContext
{
    private readonly ILogger _logger = Log.CreateLogger<OutputViewModel>();
    private readonly ReactiveProperty<bool> _isIndeterminate = new();
    private readonly ReactiveProperty<bool> _isEncoding = new();
    private readonly ReactivePropertySlim<double> _progress = new();
    private readonly ReadOnlyObservableCollection<ControllableEncodingExtension> _encoders;
    private readonly IDisposable _disposable1;
    private readonly ProjectItemContainer _itemContainer = ProjectItemContainer.Current;
    private CancellationTokenSource? _lastCts;

    public OutputViewModel(SceneFile model)
    {
        Model = model;

        Controller = SelectedEncoder.CombineLatest(DestinationFile)
            .Select(obj => obj is { First: not null, Second: not null }
                ? obj.First.CreateController(obj.Second)
                : null)
            .ToReadOnlyReactivePropertySlim();

        VideoSettings = Controller.Select(c => c?.VideoSettings)
            .DistinctUntilChanged()
            .Select(s =>
            {
                if (s == null) return null;

                s.SourceSize = new PixelSize(Model.Width, Model.Height);
                s.DestinationSize = new PixelSize(Model.Width, Model.Height);
                return new EncoderSettingsViewModel(s);
            })
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim();

        AudioSettings = Controller.Select(c => c?.AudioSettings)
            .DistinctUntilChanged()
            .Select(s => s == null ? null : new EncoderSettingsViewModel(s))
            .DisposePreviousValue()
            .ToReadOnlyReactivePropertySlim();

        CanEncode = DestinationFile.Select(x => x != null)
            .AreTrue(SelectedEncoder.Select(x => x != null))
            .ToReadOnlyReactivePropertySlim();

        _disposable1 = ExtensionProvider.Current
            .GetExtensions<ControllableEncodingExtension>()
            .AsObservableChangeSet()
            .Filter(DestinationFile.Select<string?, Func<ControllableEncodingExtension, bool>>(
                f => f == null
                    ? _ => false
                    : ext => ext.IsSupported(f)))
            .Bind(out _encoders)
            .Subscribe();
    }

    public OutputExtension Extension => SceneOutputExtension.Instance;

    public SceneFile Model { get; }

    public string TargetFile => Model.FileName;

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
            _lastCts = new CancellationTokenSource();
            _isEncoding.Value = true;
            ProgressText.Value = "";
            Started?.Invoke(this, EventArgs.Empty);

            await RenderThread.Dispatcher.InvokeAsync(async () =>
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

                    ProgressMax.Value = scene.Duration.TotalSeconds * 2;

                    EncodingController? controller = Controller.Value;
                    if (controller == null) return;
                    // フレームプロバイダー作成
                    using var renderer = new SceneRenderer(scene);
                    var frameProgress = new Subject<TimeSpan>();
                    var frameProvider = new FrameProviderImpl(scene, videoSettings.FrameRate, renderer, frameProgress);
                    // サンプルプロバイダー作成
                    using var composer = new SceneComposer(scene, renderer);
                    var sampleProgress = new Subject<TimeSpan>();
                    var sampleProvider = new SampleProviderImpl(
                        scene, composer, audioSettings.SampleRate, sampleProgress);

                    using (frameProgress.CombineLatest(sampleProgress).Subscribe(t =>
                               ProgressValue.Value = t.Item1.TotalSeconds + t.Item2.TotalSeconds))
                    {
                        RenderNodeCacheContext? cacheContext = renderer.GetCacheContext();

                        if (cacheContext != null)
                        {
                            cacheContext.CacheOptions = RenderCacheOptions.Disabled;
                        }

                        await controller.Encode(frameProvider, sampleProvider, _lastCts.Token);
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

        json[nameof(Name)] = Name.Value;
        json[nameof(DestinationFile)] = DestinationFile.Value;
        if (SelectedEncoder.Value != null)
        {
            json[nameof(SelectedEncoder)] = TypeFormat.ToString(SelectedEncoder.Value.GetType());
        }

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

        if (json.TryGetPropertyValue(nameof(Name), out JsonNode? nameNode)
            && nameNode is JsonValue nameValue
            && nameValue.TryGetValue(out string? name))
        {
            Name.Value = name;
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
