using System.Text.Json.Nodes;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

public sealed class AudioVisualizerTabViewModel : IToolContext
{
    private static readonly TimeSpan s_composeWindow = TimeSpan.FromMilliseconds(100);

    public const int DefaultFftSize = 2048;
    public const float DefaultMinDecibels = -90f;
    public const float DefaultSmoothing = 55f;
    public const SpectrumDisplayShape DefaultSpectrumShape = SpectrumDisplayShape.Bar;

    private readonly CompositeDisposable _disposables = [];
    private readonly IEditorContext _editorContext;
    private readonly IPreviewPlayer _player;
    private readonly IEditorClock _clock;
    private int _composeInFlight;

    public AudioVisualizerTabViewModel(IEditorContext editorContext)
    {
        _editorContext = editorContext;
        _player = editorContext.GetRequiredService<IPreviewPlayer>();
        _clock = editorContext.GetRequiredService<IEditorClock>();

        _player.AudioFramePushed
            .Subscribe(OnAudioFrameReceived)
            .DisposeWith(_disposables);

        _clock.CurrentTime
            .Subscribe(t => PlayheadTime.Value = t)
            .DisposeWith(_disposables);

        // Drives the paused compose path. IsSelected and SelectedMode are included so that
        // opening the tab or switching modes while paused also composes again.
        _clock.CurrentTime
            .CombineLatest(
                _player.IsPlaying,
                IsSelected,
                SelectedMode,
                (time, playing, selected, mode) => (time, playing, selected, mode))
            .Where(t => t.selected)
            .Throttle(TimeSpan.FromMilliseconds(40))
            .Subscribe(t => _ = ComposeSnapshotOnIdleAsync())
            .DisposeWith(_disposables);

        // Use Strings.Audio: the localized full name plus mode is too wide for the tab.
        Header = SelectedMode
            .Select(m => ToolTabHeaderHelper.Compose(Strings.Audio, LocalizeMode(m)))
            .ToReadOnlyReactivePropertySlim(
                ToolTabHeaderHelper.Compose(Strings.Audio, LocalizeMode(SelectedMode.Value)))
            .DisposeWith(_disposables)!;
    }

    public AudioSampleRingBuffer RingBuffer { get; } = new();

    public ReactivePropertySlim<TimeSpan> PlayheadTime { get; } = new(TimeSpan.Zero);

    public ReactivePropertySlim<AudioVisualizerMode> SelectedMode { get; } = new(AudioVisualizerMode.Waveform);

    public ReactivePropertySlim<int> FftSize { get; } = new(DefaultFftSize);

    public ReactivePropertySlim<float> MinDecibels { get; } = new(DefaultMinDecibels);

    public ReactivePropertySlim<float> Smoothing { get; } = new(DefaultSmoothing);

    public ReactivePropertySlim<SpectrumDisplayShape> SpectrumShape { get; } = new(DefaultSpectrumShape);

    public IReadOnlyList<int> AvailableFftSizes { get; } = [256, 512, 1024, 2048, 4096, 8192];

    public IReadOnlyList<SpectrumDisplayShape> AvailableSpectrumShapes { get; } = Enum.GetValues<SpectrumDisplayShape>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ToolTabExtension Extension => AudioVisualizerTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public void ResetSetting(string name)
    {
        switch (name)
        {
            case nameof(FftSize): FftSize.Value = DefaultFftSize; break;
            case nameof(MinDecibels): MinDecibels.Value = DefaultMinDecibels; break;
            case nameof(Smoothing): Smoothing.Value = DefaultSmoothing; break;
            case nameof(SpectrumShape): SpectrumShape.Value = DefaultSpectrumShape; break;
        }
    }

    private void OnAudioFrameReceived(AudioFrameSnapshot snapshot)
    {
        RingBuffer.WriteInterleaved(
            snapshot.Interleaved,
            snapshot.ChannelCount,
            snapshot.SampleRate,
            snapshot.StartTime);
    }

    // Spectrogram needs 4 s and Meter 0.4 s plus margin; the other modes need only the short
    // default window.
    private TimeSpan GetComposeWindowForMode(AudioVisualizerMode mode)
        => mode switch
        {
            AudioVisualizerMode.Spectrogram => TimeSpan.FromSeconds(4),
            AudioVisualizerMode.Meter => TimeSpan.FromMilliseconds(500),
            _ => s_composeWindow,
        };

    private async Task ComposeSnapshotOnIdleAsync()
    {
        if (_player.IsPlaying.Value) return;
        if (Interlocked.Exchange(ref _composeInFlight, 1) != 0) return;
        try
        {
            // If the playhead moves during a long compose, compose again, up to 3 times;
            // the next throttle picks up the rest.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (_player.IsPlaying.Value) break;

                TimeSpan targetTime = _clock.CurrentTime.Value;
                TimeSpan window = GetComposeWindowForMode(SelectedMode.Value);

                // Compose a window ending AT the playhead so that ReadAroundTime
                // finds the samples the user would be hearing if playback resumed.
                TimeSpan windowStart = targetTime - window;
                if (windowStart < TimeSpan.Zero) windowStart = TimeSpan.Zero;
                TimeSpan duration = targetTime - windowStart;
                if (duration <= TimeSpan.Zero) duration = window;

                AudioFrameSnapshot? snapshot = await _player
                    .ComposeAudioAsync(windowStart, duration)
                    .ConfigureAwait(false);
                if (snapshot != null)
                {
                    OnAudioFrameReceived(snapshot);
                }

                if ((_clock.CurrentTime.Value - targetTime).Duration() < TimeSpan.FromMilliseconds(10))
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // ignore cancellation from scene/project disposal
        }
        finally
        {
            Interlocked.Exchange(ref _composeInFlight, 0);
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
        RingBuffer.Clear();
    }

    internal static string LocalizeMode(AudioVisualizerMode mode) => mode switch
    {
        AudioVisualizerMode.Waveform => Strings.Waveform,
        AudioVisualizerMode.Spectrum => Strings.Spectrum,
        AudioVisualizerMode.Meter => Strings.Meter,
        AudioVisualizerMode.Spectrogram => Strings.Spectrogram,
        AudioVisualizerMode.PhaseScope => Strings.PhaseScope,
        _ => Strings.AudioVisualizer,
    };

    public object? GetService(Type serviceType)
    {
        return _editorContext.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValueAsJsonValue("mode", out int mode)
            && Enum.IsDefined(typeof(AudioVisualizerMode), mode))
        {
            SelectedMode.Value = (AudioVisualizerMode)mode;
        }

        if (json.TryGetPropertyValueAsJsonValue("fftSize", out int fft)
            && IsPowerOfTwo(fft) && fft >= 256 && fft <= 8192)
        {
            FftSize.Value = fft;
        }

        if (json.TryGetPropertyValueAsJsonValue("minDecibels", out float minDb)
            && minDb < 0f && minDb >= -120f)
        {
            MinDecibels.Value = minDb;
        }

        if (json.TryGetPropertyValueAsJsonValue("smoothing", out float smoothing)
            && smoothing >= 0f && smoothing <= 95f)
        {
            Smoothing.Value = smoothing;
        }

        if (json.TryGetPropertyValueAsJsonValue("spectrumShape", out int shape)
            && Enum.IsDefined(typeof(SpectrumDisplayShape), shape))
        {
            SpectrumShape.Value = (SpectrumDisplayShape)shape;
        }
    }

    public void WriteToJson(JsonObject json)
    {
        json["mode"] = (int)SelectedMode.Value;
        json["fftSize"] = FftSize.Value;
        json["minDecibels"] = MinDecibels.Value;
        json["smoothing"] = Smoothing.Value;
        json["spectrumShape"] = (int)SpectrumShape.Value;
    }

    private static bool IsPowerOfTwo(int v) => v > 0 && (v & (v - 1)) == 0;
}
