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
    private WaveformChunk[] _chunks = [];
    private int _previewStart;
    private int _previewCount;
    private bool _stale;
    private bool _disposed;

    public SilenceSplitViewModel(Scene scene, Element element, HistoryManager history, IPreviewPlayer? player = null)
    {
        _scene = scene;
        _element = element;
        _service = new SilenceSplitService(history);
        _player = player;
        ClipRange = element.Range;
        HasLinkedElements = scene.Groups.Any(g => g.Contains(element.Id.Value) && g.Count > 1);
        ThresholdDb.Subscribe(_ => Recalculate()).AddTo(_subscriptions);
        MinimumMilliseconds.Subscribe(_ => Recalculate()).AddTo(_subscriptions);
        PaddingMilliseconds.Subscribe(_ => Recalculate()).AddTo(_subscriptions);
        scene.Edited += OnSceneEdited;
    }

    public ReactivePropertySlim<decimal?> ThresholdDb { get; } = new(-40);
    public ReactivePropertySlim<decimal?> MinimumMilliseconds { get; } = new(300);
    public ReactivePropertySlim<decimal?> PaddingMilliseconds { get; } = new(100);
    public ReactivePropertySlim<int> ModeIndex { get; } = new(0);
    public ReactivePropertySlim<bool> IncludeLinked { get; } = new(true);
    public ReactivePropertySlim<bool> IsBusy { get; } = new(true);
    public ReactivePropertySlim<int> Progress { get; } = new(0);
    public ReactivePropertySlim<bool> CanApply { get; } = new(false);
    public ReactivePropertySlim<string> Status { get; } = new(Strings.SilenceSplit_Analyzing);
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

    public static bool CanAnalyze(Scene scene, Element element)
        => scene.Children.Contains(element) && !scene.IsElementLocked(element) && element.Length.Value > MediaTime.Zero
           && element.Objects.Any(o => o is SourceSound { Source.Value: not null } or SceneSound { ReferencedScene.Value: not null });

    public async Task AnalyzeAsync()
    {
        var ct = _lifetime.Token;
        try
        {
            int count = Math.Max(1, checked((int)Math.Ceiling(ClipRange.Duration.ToSeconds() / 0.02)));
            // Unknown chunks retain NaN; an absent source/read failure must never delete a clip.
            var chunks = Enumerable.Range(0, count).Select(i => new WaveformChunk(i, float.NaN, float.NaN)).ToArray();
            await foreach (WaveformChunk chunk in SilenceWaveform.GetChunksAsync(_scene, _element, count, ct))
            {
                chunks[chunk.Index] = chunk;
                int progress = (int)((long)(chunk.Index + 1) * 100 / count);
                if (progress > Progress.Value)
                {
                    Progress.Value = progress;
                    if (!_stale) Status.Value = $"{Strings.SilenceSplit_Analyzing} {progress}%";
                }
            }
            ct.ThrowIfCancellationRequested();
            SetWaveform(chunks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
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
        BuildPreview();
        Progress.Value = 100;
        IsBusy.Value = false;
        Recalculate();
    }

    public void ZoomPreview(bool zoomIn)
    {
        if (_chunks.Length == 0) return;
        int count = (int)Math.Clamp(zoomIn ? (_previewCount + 1L) / 2 : _previewCount * 2L,
            Math.Min(50, _chunks.Length), _chunks.Length);
        _previewStart = Math.Clamp(_previewStart + (_previewCount - count) / 2, 0, _chunks.Length - count);
        _previewCount = count;
        RefreshPreview();
    }

    public void PanPreview(double seconds)
    {
        if (_chunks.Length == 0 || !double.IsFinite(seconds)) return;
        int start = (int)Math.Clamp(Math.Round(seconds / ClipRange.Duration.ToSeconds() * _chunks.Length), 0, _chunks.Length - _previewCount);
        if (start == _previewStart) return;
        _previewStart = start;
        RefreshPreview();
    }

    public void ResetPreview()
    {
        _previewStart = 0;
        _previewCount = _chunks.Length;
        RefreshPreview();
    }

    private MediaTime PreviewPosition(int index) => new((long)((Int128)ClipRange.Duration.Flicks * index / _chunks.Length));

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
        if (_stale || IsBusy.Value) return;
        bool valid = ThresholdDb.Value is >= -120 and <= 0 && MinimumMilliseconds.Value is > 0 && PaddingMilliseconds.Value is >= 0;
        Ranges = valid ? SilenceSplitService.Plan(_scene, _element, SilenceDetector.Detect(_chunks, ClipRange.Duration,
            (double)ThresholdDb.Value.GetValueOrDefault(), MediaTime.FromSeconds((double)MinimumMilliseconds.Value.GetValueOrDefault() / 1000),
            MediaTime.FromSeconds((double)PaddingMilliseconds.Value.GetValueOrDefault() / 1000))) : [];
        CanApply.Value = valid && Ranges.Count > 0;
        Status.Value = !valid ? Strings.SilenceSplit_InvalidParameters
            : string.Format(Strings.SilenceSplit_Summary, Ranges.Count, Ranges.Sum(r => r.Duration.ToSeconds()));
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ApplyAsync()
    {
        if (!CanApply.Value || _stale || _disposed) return;
        if (_player != null) await _player.Pause();
        // The tab or scene can change while playback relinquishes its readers.
        if (!CanApply.Value || _stale || _disposed) return;
        // Applying intentionally edits the observed scene; unsubscribe before entering the transaction.
        _scene.Edited -= OnSceneEdited;
        try
        {
            _service.Apply(_scene, _element, Ranges, (SilenceSplitMode)ModeIndex.Value, IncludeLinked.Value);
            _stale = true;
            CanApply.Value = false;
            Status.Value = Strings.Completed;
        }
        finally { _scene.Edited += OnSceneEdited; }
    }

    private void OnSceneEdited(object? sender, EventArgs e)
    {
        _stale = true;
        CanApply.Value = false;
        Status.Value = Strings.SilenceSplit_Stale;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scene.Edited -= OnSceneEdited;
        _lifetime.Cancel();
        _subscriptions.Dispose();
        // AnalyzeAsync may still be unwinding; its captured token remains usable after disposal.
        _lifetime.Dispose();
    }
}
