using System.Text.Json.Nodes;
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
    private readonly AudioVisualizerTabExtension _extension;
    private int _composeInFlight;

    public AudioVisualizerTabViewModel(IEditorContext editorContext, AudioVisualizerTabExtension extension)
    {
        _editorContext = editorContext;
        _extension = extension;
        _player = editorContext.GetRequiredService<IPreviewPlayer>();
        _clock = editorContext.GetRequiredService<IEditorClock>();

        _player.AudioFramePushed
            .Subscribe(OnAudioFrameReceived)
            .DisposeWith(_disposables);

        _clock.CurrentTime
            .Subscribe(t => PlayheadTime.Value = t)
            .DisposeWith(_disposables);

        // Drives the paused compose path. Including IsSelected in the combined
        // tuple ensures that opening the tab while paused triggers an initial
        // snapshot even when CurrentTime hasn't changed since.
        _clock.CurrentTime
            .CombineLatest(
                _player.IsPlaying,
                IsSelected,
                (time, playing, selected) => (time, playing, selected))
            .Where(t => t.selected)
            .Throttle(TimeSpan.FromMilliseconds(40))
            .Subscribe(t => _ = ComposeSnapshotOnIdleAsync(t.time, t.playing))
            .DisposeWith(_disposables);
    }

    public event EventHandler? SnapshotUpdated;

    public AudioSampleRingBuffer RingBuffer { get; } = new();

    public ReactivePropertySlim<TimeSpan> PlayheadTime { get; } = new(TimeSpan.Zero);

    public ReactivePropertySlim<AudioVisualizerMode> SelectedMode { get; } = new(AudioVisualizerMode.Waveform);

    public ReactivePropertySlim<int> FftSize { get; } = new(DefaultFftSize);

    public ReactivePropertySlim<float> MinDecibels { get; } = new(DefaultMinDecibels);

    public ReactivePropertySlim<float> Smoothing { get; } = new(DefaultSmoothing);

    public ReactivePropertySlim<SpectrumDisplayShape> SpectrumShape { get; } = new(DefaultSpectrumShape);

    public IReadOnlyList<int> AvailableFftSizes { get; } = [256, 512, 1024, 2048, 4096, 8192];

    public IReadOnlyList<SpectrumDisplayShape> AvailableSpectrumShapes { get; } = Enum.GetValues<SpectrumDisplayShape>();

    public string Header => Strings.AudioVisualizer;

    public ToolTabExtension Extension => _extension;

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
        SnapshotUpdated?.Invoke(this, EventArgs.Empty);
    }

    private async Task ComposeSnapshotOnIdleAsync(TimeSpan currentTime, bool playing)
    {
        if (playing) return;
        if (Interlocked.Exchange(ref _composeInFlight, 1) != 0) return;
        try
        {
            // Compose a window ending AT the playhead so that ReadAroundTime
            // finds the samples the user would be hearing if playback resumed.
            TimeSpan windowStart = currentTime - s_composeWindow;
            if (windowStart < TimeSpan.Zero) windowStart = TimeSpan.Zero;
            TimeSpan duration = currentTime - windowStart;
            if (duration <= TimeSpan.Zero) duration = s_composeWindow;

            AudioFrameSnapshot? snapshot = await _player
                .ComposeAudioAsync(windowStart, duration)
                .ConfigureAwait(false);
            if (snapshot != null)
            {
                OnAudioFrameReceived(snapshot);
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

    public object? GetService(Type serviceType)
    {
        return _editorContext.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue("mode", out var modeNode) && modeNode is JsonValue modeValue
            && modeValue.TryGetValue(out int mode) && Enum.IsDefined(typeof(AudioVisualizerMode), mode))
        {
            SelectedMode.Value = (AudioVisualizerMode)mode;
        }

        if (json.TryGetPropertyValue("fftSize", out var fftNode) && fftNode is JsonValue fftValue
            && fftValue.TryGetValue(out int fft) && IsPowerOfTwo(fft) && fft >= 256 && fft <= 8192)
        {
            FftSize.Value = fft;
        }

        if (json.TryGetPropertyValue("minDecibels", out var minDbNode) && minDbNode is JsonValue minDbValue
            && minDbValue.TryGetValue(out float minDb) && minDb < 0f && minDb >= -120f)
        {
            MinDecibels.Value = minDb;
        }

        if (json.TryGetPropertyValue("smoothing", out var smoothingNode) && smoothingNode is JsonValue smoothingValue
            && smoothingValue.TryGetValue(out float smoothing) && smoothing >= 0f && smoothing <= 95f)
        {
            Smoothing.Value = smoothing;
        }

        if (json.TryGetPropertyValue("spectrumShape", out var shapeNode) && shapeNode is JsonValue shapeValue
            && shapeValue.TryGetValue(out int shape) && Enum.IsDefined(typeof(SpectrumDisplayShape), shape))
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
