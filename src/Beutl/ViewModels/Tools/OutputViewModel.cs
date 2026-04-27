using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private string? _activeDestination;

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

    public ReactiveProperty<string> ProgressMain { get; } = new(string.Empty);

    public ReactiveProperty<string> ProgressSub { get; } = new(string.Empty);

    public ReactiveProperty<string> Elapsed { get; } = new("00:00:00");

    public ReactiveProperty<string> Eta { get; } = new("--:--:--");

    public ReactiveProperty<string> CurrentSize { get; } = new("0 MB");

    public ReactiveProperty<string> EstimatedSize { get; } = new("-- MB");

    public ReactiveProperty<long> CurrentFrame { get; } = new();

    public ReactiveProperty<long> TotalFrames { get; } = new();

    public ReactiveProperty<string> FrameProgressText { get; } = new("0 / 0");

    public ReactiveProperty<bool> IsCompleted { get; } = new();

    public ReactiveProperty<bool> WasCancelled { get; } = new();

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
        var stopwatch = new Stopwatch();
        bool succeeded = false;
        try
        {
            _logger.LogInformation("Starting encoding process.");
            LogEncodingSettings();
            _lastCts = new CancellationTokenSource();
            _isEncoding.Value = true;
            IsCompleted.Value = false;
            WasCancelled.Value = false;
            ProgressText.Value = "";
            ProgressMain.Value = Strings.Encoding;
            ProgressSub.Value = string.Empty;
            Elapsed.Value = "00:00:00";
            Eta.Value = "--:--:--";
            CurrentSize.Value = "0 MB";
            EstimatedSize.Value = "-- MB";
            CurrentFrame.Value = 0;
            TotalFrames.Value = 0;
            FrameProgressText.Value = "0 / 0";
            _activeDestination = DestinationFile.Value;
            Started?.Invoke(this, EventArgs.Empty);

            stopwatch.Start();

            await Task.Run(async () =>
            {
                _isIndeterminate.Value = false;
                if (VideoSettings.Value?.Settings is not VideoEncoderSettings videoSettings
                    || AudioSettings.Value?.Settings is not AudioEncoderSettings audioSettings)
                {
                    ProgressText.Value = MessageStrings.UnexpectedError;
                    _logger.LogWarning("Encoder settings are null. (Encoder: {Encoder})", SelectedEncoder.Value);
                    return;
                }

                videoSettings.SourceSize = Model.FrameSize;

                ProgressMax.Value = Model.Duration.TotalSeconds * 2;

                double frameRate = videoSettings.FrameRate.ToDouble();
                if (!double.IsFinite(frameRate) || frameRate <= 0) frameRate = 30;
                long totalFrames = (long)Math.Round(Model.Duration.TotalSeconds * frameRate);
                TotalFrames.Value = totalFrames;
                FrameProgressText.Value = $"0 / {totalFrames}";

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

                ClearEditViewModelCaches();

                using var renderer = new SceneRenderer(Model, disableResourceShare: true);
                renderer.CacheOptions = RenderCacheOptions.Disabled;
                var frameProgress = new Subject<TimeSpan>();
                using var frameProvider = new FrameProviderImpl(Model, videoSettings.FrameRate, renderer, frameProgress);
                using var composer = new SceneComposer(Model, disableResourceShare: true) { SampleRate = audioSettings.SampleRate };
                var sampleProgress = new Subject<TimeSpan>();
                using var sampleProvider = new SampleProviderImpl(
                    Model, composer, audioSettings.SampleRate, sampleProgress);

                string? destinationPath = _activeDestination;

                using (frameProgress
                           .Subscribe(t =>
                           {
                               long frame = (long)Math.Round(t.TotalSeconds * frameRate);
                               CurrentFrame.Value = frame;
                               FrameProgressText.Value = totalFrames > 0
                                   ? $"{frame} / {totalFrames}"
                                   : $"{frame}";
                           }))
                using (frameProgress.CombineLatest(sampleProgress)
                           .Subscribe(t =>
                           {
                               double value = t.Item1.TotalSeconds + t.Item2.TotalSeconds;
                               ProgressValue.Value = value;
                               UpdateProgressIndicators(stopwatch.Elapsed, value, ProgressMax.Value, destinationPath);
                           }))
                {
                    await controller.Encode(frameProvider, sampleProvider, _lastCts.Token);
                }
            });

            succeeded = !(_lastCts?.IsCancellationRequested ?? true);
            if (succeeded)
            {
                ProgressValue.Value = ProgressMax.Value;
                CurrentFrame.Value = TotalFrames.Value;
                FrameProgressText.Value = TotalFrames.Value > 0
                    ? $"{TotalFrames.Value} / {TotalFrames.Value}"
                    : FrameProgressText.Value;
            }
            ProgressText.Value = Strings.Completed;
            ProgressMain.Value = Strings.Completed;
            ProgressSub.Value = string.Empty;
            Eta.Value = "00:00:00";
            _logger.LogInformation("Encoding process completed successfully.");
        }
        catch (OperationCanceledException)
        {
            WasCancelled.Value = true;
            ProgressText.Value = Strings.Cancel;
            ProgressMain.Value = Strings.Cancel;
            _logger.LogInformation("Encoding cancelled.");
            TryDeletePartialFile(_activeDestination);
        }
        catch (Exception ex)
        {
            NotificationService.ShowError(MessageStrings.OutputException, ex.Message);
            _logger.LogError(ex, "An exception occurred during the encoding process.");
        }
        finally
        {
            stopwatch.Stop();
            Elapsed.Value = FormatDuration(stopwatch.Elapsed);
            _progress.Value = 0;
            _isIndeterminate.Value = false;
            _isEncoding.Value = false;
            IsCompleted.Value = succeeded;
            string? completedPath = _activeDestination;
            _activeDestination = null;
            _lastCts = null;
            Finished?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("Encoding process finished.");

            if (succeeded && completedPath != null)
            {
                ShowCompletionNotification(completedPath);
            }
        }
    }

    private void UpdateProgressIndicators(TimeSpan elapsed, double value, double max, string? destinationPath)
    {
        Elapsed.Value = FormatDuration(elapsed);

        long currentBytes = 0;
        if (destinationPath != null)
        {
            try
            {
                var info = new FileInfo(destinationPath);
                if (info.Exists) currentBytes = info.Length;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read partial file size during encoding.");
            }
        }

        CurrentSize.Value = FormatBytes(currentBytes);

        if (max > 0 && value > 0)
        {
            double ratio = Math.Clamp(value / max, 0.0, 1.0);
            if (ratio > 0 && ratio < 1)
            {
                double etaSeconds = elapsed.TotalSeconds * (1.0 / ratio - 1.0);
                if (double.IsFinite(etaSeconds) && etaSeconds >= 0)
                {
                    Eta.Value = FormatDuration(TimeSpan.FromSeconds(etaSeconds));
                }
            }
            else if (ratio >= 1)
            {
                Eta.Value = "00:00:00";
            }

            if (currentBytes > 0 && ratio > 0)
            {
                long estimatedTotal = (long)(currentBytes / ratio);
                EstimatedSize.Value = FormatBytes(estimatedTotal);
            }
        }

        ProgressSub.Value = $"{Elapsed.Value} / {Eta.Value}";
    }

    private static string FormatDuration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        if (span.TotalHours >= 100) return @"99:59:59";
        return span.ToString(@"hh\:mm\:ss");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 MB";
        const double kb = 1024.0;
        const double mb = kb * 1024.0;
        const double gb = mb * 1024.0;
        return bytes switch
        {
            < (long)mb => $"{bytes / kb:0.0} KB",
            < (long)gb => $"{bytes / mb:0.0} MB",
            _ => $"{bytes / gb:0.00} GB"
        };
    }

    private void TryDeletePartialFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!string.Equals(path, DestinationFile.Value, StringComparison.Ordinal))
        {
            _logger.LogDebug("Skip deleting partial file because destination changed.");
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                _logger.LogInformation("Deleted partial output file: {Path}", path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete partial output file: {Path}", path);
        }
    }

    private void ShowCompletionNotification(string path)
    {
        try
        {
            string fileName = Path.GetFileName(path);
            NotificationService.ShowSuccess(
                Strings.Completed,
                string.Format(MessageStrings.ExportCompleted_File, fileName),
                expiration: TimeSpan.FromSeconds(5),
                onActionButtonClick: () => OpenContainingFolder(path),
                actionButtonText: Strings.OpenFolder);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show completion notification.");
        }
    }

    public void OpenContainingFolder()
    {
        if (!string.IsNullOrEmpty(DestinationFile.Value))
        {
            OpenContainingFolder(DestinationFile.Value);
        }
    }

    public void PlayOutput()
    {
        if (string.IsNullOrEmpty(DestinationFile.Value)) return;
        OpenWithDefaultApp(DestinationFile.Value);
    }

    private void OpenContainingFolder(string filePath)
    {
        try
        {
            string? folder = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(folder)) return;

            if (OperatingSystem.IsWindows())
            {
                if (File.Exists(filePath))
                {
                    var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                    psi.ArgumentList.Add($"/select,{filePath}");
                    Process.Start(psi);
                }
                else
                {
                    Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                if (File.Exists(filePath))
                {
                    psi.ArgumentList.Add("-R");
                    psi.ArgumentList.Add(filePath);
                }
                else
                {
                    psi.ArgumentList.Add(folder);
                }
                Process.Start(psi);
            }
            else
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open containing folder for {Path}", filePath);
        }
    }

    private void OpenWithDefaultApp(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add(filePath);
                Process.Start(psi);
            }
            else
            {
                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true, Verb = "open" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open output file {Path}", filePath);
        }
    }

    private static void ClearEditViewModelCaches()
    {
        foreach (EditorTabItem item in EditorService.Current.TabItems)
        {
            if (item.Context.Value is EditViewModel editViewModel)
            {
                editViewModel.Renderer.Value.ClearAllCaches();
                editViewModel.FrameCacheManager.Value.Clear();
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
            string path = Model.Uri!.LocalPath;
            DestinationFile.Value = Path.Combine(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}.mp4");
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
