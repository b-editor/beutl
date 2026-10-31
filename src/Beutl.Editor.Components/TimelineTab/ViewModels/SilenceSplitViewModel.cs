using Beutl.Audio;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using Beutl.ProjectSystem;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed class SilenceSplitViewModel : IDisposable
{
    private readonly Scene _scene;
    private readonly Element _element;
    private readonly SilenceSplitService _service;
    private readonly IPreviewPlayer? _player;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly System.Reactive.Disposables.CompositeDisposable _subscriptions = [];
    private readonly HashSet<Scene> _observedScenes = [];
    private WaveformChunk[] _chunks = [];
    private int _previewStart;
    private int _previewCount;
    private double _previewZoomCount;
    private double _previewOffset;
    private bool _stale;
    private bool _disposed;
    private bool _applying;

    public SilenceSplitViewModel(Scene scene, Element element, HistoryManager history, IPreviewPlayer? player = null)
    {
        _scene = scene;
        _element = element;
        _service = new SilenceSplitService(history, player);
        _player = player;
        ClipRange = element.Range;
        HasLinkedElements = scene.Groups.Any(g => g.Contains(element.Id.Value) && g.Count > 1);
        ThresholdDb.Subscribe(_ => Recalculate()).AddTo(_subscriptions);
        MinimumMilliseconds.Subscribe(_ => Recalculate()).AddTo(_subscriptions);
        PaddingMilliseconds.Subscribe(_ => Recalculate()).AddTo(_subscriptions);
        ObserveScene(scene);
    }

    public ReactivePropertySlim<decimal?> ThresholdDb { get; } = new(-40);
    public ReactivePropertySlim<decimal?> MinimumMilliseconds { get; } = new(300);
    public ReactivePropertySlim<decimal?> PaddingMilliseconds { get; } = new(100);
    public ReactivePropertySlim<int> ModeIndex { get; } = new(0);
    public ReactivePropertySlim<bool> IncludeLinked { get; } = new(true);
    public ReactivePropertySlim<bool> IsBusy { get; } = new(false);
    public ReactivePropertySlim<int> Progress { get; } = new(0);
    public ReactivePropertySlim<bool> IsProgressIndeterminate { get; } = new();
    public ReactivePropertySlim<bool> CanApply { get; } = new(false);
    public ReactivePropertySlim<string> Status { get; } = new(Strings.SilenceSplit_RefreshToAnalyze);
    public bool HasLinkedElements { get; }
    public TimeRange ClipRange { get; }
    public IReadOnlyList<TimeRange> Ranges { get; private set; } = [];
    public IReadOnlyList<WaveformChunk> PreviewChunks { get; private set; } = [];
    public bool CanZoomIn => _previewCount > Math.Min(50, _chunks.Length);
    public bool CanZoomOut => _previewCount < _chunks.Length;
    // Preview coordinates are clip-local. Zooming never changes the full-clip detection or cuts.
    public TimeRange PreviewRange => _chunks.Length == 0 ? new(MediaTime.Zero, ClipRange.Duration)
        : TimeRange.FromRange(PreviewPosition(_previewStart), PreviewPosition(_previewStart + _previewCount));
    public event EventHandler? PreviewChanged;
    internal CancellationToken LifetimeToken => _lifetime.Token;

    public static bool CanAnalyze(Scene scene, Element element)
        => scene.Children.Contains(element) && !scene.IsElementLocked(element) && element.Length.Value > MediaTime.Zero
           && element.Objects.Any(HasAudioSource);

    private static bool HasAudioSource(EngineObject obj) => obj switch
    {
        SourceSound sound => sound.Source.Value != null || sound.Source.HasExpression,
        SceneSound sound => sound.ReferencedScene.Value != null || sound.ReferencedScene.HasExpression,
        SoundGroup group => group.Children.Any(HasAudioSource),
        _ => false
    };

    public async Task AnalyzeAsync()
    {
        IsBusy.Value = true;
        Status.Value = Strings.SilenceSplit_Analyzing;
        var ct = _lifetime.Token;
        try
        {
            TaskScheduler owner = SynchronizationContext.Current is null
                ? TaskScheduler.Current : TaskScheduler.FromCurrentSynchronizationContext();
            int count = Math.Max(1, checked((int)Math.Ceiling(ClipRange.Duration.ToSeconds() / 0.02)));
            // Unknown chunks retain NaN; an absent source/read failure must never delete a clip.
            var chunks = Enumerable.Range(0, count).Select(i => new WaveformChunk(i, float.NaN, float.NaN)).ToArray();
            await ReadChunksAsync();
            ct.ThrowIfCancellationRequested();
            SetWaveform(chunks);

            async Task ReadChunksAsync()
            {
                int reported = 0;
                // Keep the million-bin enumeration off the UI context. Only percentage changes
                // return to its scheduler, and are awaited so none can arrive after completion.
                await foreach (WaveformChunk chunk in SilenceWaveform.GetChunksAsync(_scene, _element, count, ct, ObserveScene).ConfigureAwait(false))
                {
                    chunks[chunk.Index] = chunk;
                    int progress = (int)((long)(chunk.Index + 1) * 100 / count);
                    if (progress <= reported) continue;
                    reported = progress;
                    await Task.Factory.StartNew(() =>
                    {
                        if (_disposed) return;
                        Progress.Value = progress;
                        if (!_stale) Status.Value = $"{Strings.SilenceSplit_Analyzing} {progress}%";
                    }, ct, TaskCreationOptions.DenyChildAttach, owner).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { if (!_disposed) IsBusy.Value = false; }
        catch (Exception ex)
        {
            IsBusy.Value = false;
            CanApply.Value = false;
            Status.Value = string.Format(Strings.SilenceSplit_Failed, ex.Message);
        }
    }

    internal void SetWaveform(WaveformChunk[] chunks)
    {
        _chunks = chunks;
        _previewStart = 0;
        _previewCount = chunks.Length;
        _previewZoomCount = _previewCount;
        _previewOffset = 0;
        BuildPreview();
        Progress.Value = 100;
        IsBusy.Value = false;
        Recalculate();
    }

    public void ZoomPreview(bool zoomIn) => ZoomPreview(zoomIn ? 2 : 0.5, 0.5);

    public void ZoomPreview(double factor, double anchor)
    {
        if (_chunks.Length == 0 || !double.IsFinite(factor) || factor <= 0 || !double.IsFinite(anchor)) return;
        // Preserve fractional input so small touchpad deltas accumulate across 20 ms bins.
        _previewZoomCount = Math.Clamp(_previewZoomCount / factor, Math.Min(50, _chunks.Length), _chunks.Length);
        int count = (int)Math.Ceiling(_previewZoomCount);
        if (count == _previewCount) return;
        _previewStart = Math.Clamp(_previewStart + (int)Math.Round((_previewCount - count) * Math.Clamp(anchor, 0, 1)),
            0, _chunks.Length - count);
        _previewCount = count;
        _previewOffset = PreviewPosition(_previewStart).ToSeconds();
        RefreshPreview();
    }

    public void ScrollPreview(double seconds) => PanPreview(_previewOffset + seconds);

    public void PanPreview(double seconds)
    {
        if (_chunks.Length == 0 || !double.IsFinite(seconds)) return;
        _previewOffset = Math.Clamp(seconds, 0, PreviewPosition(_chunks.Length - _previewCount).ToSeconds());
        int start = (int)Math.Clamp(Math.Round(_previewOffset / ClipRange.Duration.ToSeconds() * _chunks.Length), 0, _chunks.Length - _previewCount);
        if (start == _previewStart) return;
        _previewStart = start;
        RefreshPreview();
    }

    public void ResetPreview()
    {
        _previewStart = 0;
        _previewCount = _chunks.Length;
        _previewZoomCount = _previewCount;
        _previewOffset = 0;
        RefreshPreview();
    }

    private MediaTime PreviewPosition(int index) => SilenceDetector.BinStart(ClipRange.Duration, index, _chunks.Length);

    private void RefreshPreview()
    {
        BuildPreview();
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BuildPreview()
    {
        // Only the visible window is reduced, with bounded drawing cost at every zoom level.
        int count = Math.Min(_previewCount, 1200);
        var preview = new WaveformChunk[count];
        for (int i = 0; i < count; i++)
        {
            float min = 0, max = 0;
            int start = _previewStart + (int)((long)i * _previewCount / count);
            int end = _previewStart + (int)((long)(i + 1) * _previewCount / count);
            for (int j = start; j < end; j++)
            {
                if (!float.IsFinite(_chunks[j].MinValue) || !float.IsFinite(_chunks[j].MaxValue)) continue;
                min = Math.Min(min, _chunks[j].MinValue);
                max = Math.Max(max, _chunks[j].MaxValue);
            }
            preview[i] = new(i, min, max);
        }
        PreviewChunks = preview;
    }

    private void Recalculate()
    {
        if (_stale || IsBusy.Value || _chunks.Length == 0) return;
        bool valid = ThresholdDb.Value is >= -120 and <= 0 && MinimumMilliseconds.Value is > 0 && PaddingMilliseconds.Value is >= 0;
        Ranges = valid ? SilenceSplitService.Plan(_scene, _element, SilenceDetector.Detect(_chunks, ClipRange.Duration,
            (double)ThresholdDb.Value.GetValueOrDefault(), MediaTime.FromSeconds((double)MinimumMilliseconds.Value.GetValueOrDefault() / 1000),
            MediaTime.FromSeconds((double)PaddingMilliseconds.Value.GetValueOrDefault() / 1000))) : [];
        CanApply.Value = valid && Ranges.Count > 0;
        Status.Value = !valid ? Strings.SilenceSplit_InvalidParameters
            : string.Format(Strings.SilenceSplit_Summary, Ranges.Count, Ranges.Sum(r => r.Duration.ToSeconds()));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> ApplyAsync()
    {
        if (!CanApply.Value || _stale || _disposed || _applying) return false;
        _applying = true;
        IsBusy.Value = true;
        Progress.Value = 0;
        IsProgressIndeterminate.Value = true;
        Status.Value = Strings.SilenceSplit_Applying;
        try
        {
            if (_player != null) await _player.Pause();
            // The tab or scene can change while playback relinquishes its readers.
            if (!CanApply.Value || _stale || _disposed) return false;
            // Keep observing during preparation, including failures. Publication invalidates these
            // ranges too, but a failed preparation with no document edits can still be retried.
            var progress = new Progress<int>(value =>
            {
                // Progress callbacks are posted to the owning context and may arrive after the
                // operation or tab has ended. Never overwrite its final status with a late update.
                if (!_applying || _stale || _disposed) return;
                Progress.Value = value;
                IsProgressIndeterminate.Value = value == 100;
                Status.Value = value == 100 ? Strings.SilenceSplit_Applying : $"{Strings.SilenceSplit_Preparing} {value}%";
            });
            bool applied = await _service.ApplyAsync(_scene, _element, Ranges, (SilenceSplitMode)ModeIndex.Value,
                IncludeLinked.Value, _lifetime.Token, progress);
            if (_disposed) return false;
            _stale = true;
            CanApply.Value = false;
            if (applied) Progress.Value = 100;
            Status.Value = applied ? Strings.Completed : Strings.SilenceSplit_Stale;
            return applied;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        finally
        {
            _applying = false;
            if (!_disposed)
            {
                IsBusy.Value = false;
                IsProgressIndeterminate.Value = false;
            }
        }
    }

    private void OnSceneEdited(object? sender, EventArgs e)
    {
        if (_stale) return;
        _stale = true;
        CanApply.Value = false;
        Status.Value = Strings.SilenceSplit_Stale;
        if (!_applying) _lifetime.Cancel();
    }

    private void ObserveScene(Scene scene)
    {
        if (_observedScenes.Add(scene)) scene.Edited += OnSceneEdited;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Scene scene in _observedScenes) scene.Edited -= OnSceneEdited;
        _observedScenes.Clear();
        _lifetime.Cancel();
        _subscriptions.Dispose();
        // AnalyzeAsync may still be unwinding; its captured token remains usable after disposal.
        _lifetime.Dispose();
    }
}
