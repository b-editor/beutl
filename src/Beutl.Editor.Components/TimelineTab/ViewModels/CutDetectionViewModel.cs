using Beutl.Configuration;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed class CutDetectionViewModel : IDisposable
{
    private readonly Scene _scene;
    private readonly Element _element;
    private readonly CutDetectionService _service;
    private readonly IPreviewPlayer? _player;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CompositeDisposable _subscriptions = [];
    private CutDetectionResult? _result;
    private int _previewStart;
    private int _previewCount;
    private double _previewZoomCount;
    private double _previewOffset;
    private bool _stale;
    private bool _disposed;
    private bool _applying;

    public CutDetectionViewModel(Scene scene, Element element, HistoryManager history, EditorConfig configuration,
        IPreviewPlayer? player = null, DecoderRegistry? decoders = null)
    {
        _scene = scene;
        _element = element;
        _service = new CutDetectionService(history, configuration, decoders);
        _player = player;
        ClipRange = element.Range;
        HasLinkedElements = scene.Groups.Any(group => group.Contains(element.Id.Value) && group.Count > 1);
        Threshold.Subscribe(_ => Recalculate()).AddTo(_subscriptions);
        MinimumMilliseconds.Subscribe(_ => Recalculate()).AddTo(_subscriptions);
        scene.Edited += OnSceneEdited;
    }

    public ReactivePropertySlim<decimal?> Threshold { get; } = new(0.3m);
    public ReactivePropertySlim<decimal?> MinimumMilliseconds { get; } = new(500);
    public ReactivePropertySlim<int> ModeIndex { get; } = new(0);
    public ReactivePropertySlim<bool> IncludeLinked { get; } = new(true);
    public ReactivePropertySlim<bool> IsBusy { get; } = new(false);
    public ReactivePropertySlim<int> Progress { get; } = new(0);
    public ReactivePropertySlim<bool> IsProgressIndeterminate { get; } = new();
    public ReactivePropertySlim<bool> CanApply { get; } = new(false);
    public ReactivePropertySlim<string> Status { get; } = new(Strings.CutDetection_RefreshToAnalyze);
    public bool HasLinkedElements { get; }
    public TimeRange ClipRange { get; }
    public IReadOnlyList<MediaTime> Candidates { get; private set; } = [];
    internal IReadOnlyList<CutFrameScore> Frames { get; private set; } = [];
    internal CancellationToken LifetimeToken => _lifetime.Token;
    public bool CanZoomIn => _previewCount > Math.Min(50, Frames.Count);
    public bool CanZoomOut => _previewCount < Frames.Count;
    // Preview coordinates are clip-local. Zooming never changes the full-clip detection or cuts.
    public TimeRange PreviewRange => Frames.Count == 0 ? new(MediaTime.Zero, ClipRange.Duration)
        : TimeRange.FromRange(PreviewPosition(_previewStart), PreviewPosition(_previewStart + _previewCount));
    public event EventHandler? PreviewChanged;

    public static bool CanAnalyze(Scene scene, Element element)
        => CutDetectionService.CanAnalyze(scene, element);

    public async Task AnalyzeAsync()
    {
        if (_disposed) return;
        IsBusy.Value = true;
        Progress.Value = 0;
        Status.Value = Strings.CutDetection_Analyzing;
        CancellationToken token = _lifetime.Token;
        try
        {
            if (_player != null) await _player.Pause();
            if (_disposed || _stale || token.IsCancellationRequested) return;
            // Progress posts to the UI context and is ignored once a scene edit has made this
            // analysis stale, so a late update cannot replace the stale message.
            var progress = new Progress<int>(value =>
            {
                if (_disposed || _stale || token.IsCancellationRequested) return;
                Progress.Value = value;
                Status.Value = $"{Strings.CutDetection_Analyzing} {value}%";
            });
            CutDetectionResult result = await _service.AnalyzeAsync(_scene, _element, token, progress);
            if (_disposed || _stale || token.IsCancellationRequested) return;
            SetResult(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (_disposed) return;
            CanApply.Value = false;
            Status.Value = string.Format(Strings.CutDetection_Failed, ex.Message);
        }
        finally
        {
            if (!_disposed) IsBusy.Value = false;
        }
    }

    internal void SetResult(CutDetectionResult result)
    {
        _result = result;
        Frames = result.Frames;
        _previewStart = 0;
        _previewCount = Frames.Count;
        _previewZoomCount = _previewCount;
        _previewOffset = 0;
        Progress.Value = 100;
        IsBusy.Value = false;
        Recalculate();
    }

    public void ZoomPreview(bool zoomIn) => ZoomPreview(zoomIn ? 2 : 0.5, 0.5);

    public void ZoomPreview(double factor, double anchor)
    {
        if (Frames.Count == 0 || !double.IsFinite(factor) || factor <= 0 || !double.IsFinite(anchor)) return;
        // Preserve fractional input so small touchpad deltas accumulate across samples.
        _previewZoomCount = Math.Clamp(_previewZoomCount / factor, Math.Min(50, Frames.Count), Frames.Count);
        int count = (int)Math.Ceiling(_previewZoomCount);
        if (count == _previewCount) return;
        _previewStart = Math.Clamp(_previewStart + (int)Math.Round((_previewCount - count) * Math.Clamp(anchor, 0, 1)),
            0, Frames.Count - count);
        _previewCount = count;
        _previewOffset = PreviewPosition(_previewStart).ToSeconds();
        RefreshPreview();
    }

    public void ScrollPreview(double seconds) => PanPreview(_previewOffset + seconds);

    public void PanPreview(double seconds)
    {
        if (Frames.Count == 0 || !double.IsFinite(seconds)) return;
        _previewOffset = Math.Clamp(seconds, 0, PreviewPosition(Frames.Count - _previewCount).ToSeconds());
        int start = (int)Math.Clamp(Math.Round(_previewOffset / ClipRange.Duration.ToSeconds() * Frames.Count),
            0, Frames.Count - _previewCount);
        if (start == _previewStart) return;
        _previewStart = start;
        RefreshPreview();
    }

    public void ResetPreview()
    {
        _previewStart = 0;
        _previewCount = Frames.Count;
        _previewZoomCount = _previewCount;
        _previewOffset = 0;
        RefreshPreview();
    }

    private MediaTime PreviewPosition(int index)
        => Frames.Count == 0 ? MediaTime.Zero
            : new MediaTime((long)((Int128)index * ClipRange.Duration.Flicks / Frames.Count));

    private void RefreshPreview()
    {
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Recalculate()
    {
        if (_stale || IsBusy.Value || _result == null || _disposed) return;
        bool valid = Threshold.Value is >= 0.01m and <= 1m && MinimumMilliseconds.Value is > 0;
        Candidates = valid
            ? _result.GetCuts((double)Threshold.Value.GetValueOrDefault(),
                MediaTime.FromSeconds((double)MinimumMilliseconds.Value.GetValueOrDefault() / 1000))
            : [];
        CanApply.Value = valid && Candidates.Count > 0;
        Status.Value = !valid ? Strings.CutDetection_InvalidParameters
            : Candidates.Count == 0 ? Strings.CutDetection_None
            : string.Format(Strings.CutDetection_Summary, Candidates.Count);
        RefreshPreview();
    }

    public Task<bool> ApplyAsync()
        => ModeIndex.Value == 1 ? ApplyMarkersAsync() : ApplySplitAsync();

    public async Task<bool> ApplySplitAsync()
    {
        if (!CanApply.Value || _stale || _disposed || _applying) return false;
        _applying = true;
        IsBusy.Value = true;
        Progress.Value = 0;
        IsProgressIndeterminate.Value = true;
        Status.Value = Strings.CutDetection_Applying;
        try
        {
            if (_player != null) await _player.Pause();
            if (!CanApply.Value || _stale || _disposed) return false;
            bool applied = _service.ApplySplit(_scene, _element, Candidates, IncludeLinked.Value, _lifetime.Token);
            if (_disposed) return false;
            _stale = true;
            CanApply.Value = false;
            Status.Value = applied ? Strings.Completed : Strings.CutDetection_Stale;
            return applied;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            if (!_disposed) Status.Value = string.Format(Strings.CutDetection_Failed, ex.Message);
            return false;
        }
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

    public async Task<bool> ApplyMarkersAsync()
    {
        if (!CanApply.Value || _stale || _disposed || _applying) return false;
        _applying = true;
        IsBusy.Value = true;
        Status.Value = Strings.CutDetection_Applying;
        try
        {
            if (_player != null) await _player.Pause();
            if (!CanApply.Value || _stale || _disposed) return false;
            int added = _service.AddMarkers(_scene, Candidates, _lifetime.Token);
            if (_disposed) return false;
            _stale = true;
            CanApply.Value = false;
            Status.Value = added > 0 ? string.Format(Strings.CutDetection_MarkersAdded, added)
                : Strings.CutDetection_None;
            return added > 0;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            if (!_disposed) Status.Value = string.Format(Strings.CutDetection_Failed, ex.Message);
            return false;
        }
        finally
        {
            _applying = false;
            if (!_disposed) IsBusy.Value = false;
        }
    }

    private void OnSceneEdited(object? sender, EventArgs e)
    {
        if (_stale) return;
        _stale = true;
        CanApply.Value = false;
        Status.Value = Strings.CutDetection_Stale;
        if (!_applying) _lifetime.Cancel();
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
