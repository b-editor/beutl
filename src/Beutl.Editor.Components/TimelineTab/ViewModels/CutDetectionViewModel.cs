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
        Threshold.Subscribe(value =>
        {
            if (value is { } threshold)
            {
                double sliderValue = (double)threshold;
                if (Math.Abs(ThresholdSlider.Value - sliderValue) > 0.0005) ThresholdSlider.Value = sliderValue;
            }

            Recalculate();
        }).AddTo(_subscriptions);
        ThresholdSlider.Subscribe(value =>
        {
            decimal threshold = (decimal)Math.Round(value, 2);
            if (Threshold.Value != threshold) Threshold.Value = threshold;
        }).AddTo(_subscriptions);
        MinimumMilliseconds.Subscribe(_ => Recalculate()).AddTo(_subscriptions);
        scene.Edited += OnSceneEdited;
    }

    public ReactivePropertySlim<decimal?> Threshold { get; } = new(0.3m);
    public ReactivePropertySlim<double> ThresholdSlider { get; } = new(0.3);
    public ReactivePropertySlim<decimal?> MinimumMilliseconds { get; } = new(500);
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
        Progress.Value = 100;
        IsBusy.Value = false;
        Recalculate();
    }

    public void CancelAnalysis()
    {
        if (_disposed || _applying || !IsBusy.Value) return;
        _lifetime.Cancel();
        IsBusy.Value = false;
        Progress.Value = 0;
        Status.Value = Strings.CutDetection_Canceled;
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
        PreviewChanged?.Invoke(this, EventArgs.Empty);
    }

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
