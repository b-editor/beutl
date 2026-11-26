using System.Diagnostics;
using System.Reactive;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Proxy;
using Beutl.Media.Source;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;
using ExtensionGuard = Beutl.Api.Services.ExtensionGuard;
using WriteableBitmap = Avalonia.Media.Imaging.WriteableBitmap;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class ElementViewModel
{
    // Media detail requires at least this much horizontal space.
    internal const double MinimumThumbnailWidth = 25;
    private const int MaxWaveformSamplesPerChunk = 4096;
    private bool _timelineViewAttached;
    private int _thumbnailLayoutVersion;
    private int _thumbnailCountVersion;
    private int _failedThumbnailLayoutVersion = -1;
    private bool _isEditPreviewActive;
    private bool _holdPreviewUntilThumbnailsRefresh;
    private bool _thumbnailRefreshPending;
    private MediaPreviewMapping? _previewMapping;
    private MediaPreviewMapping? _decodingPreviewMapping;
    private MediaPreviewMapping? _displayedPreviewMapping;
    private MediaTime _previewLayoutDuration;
    private double _previewInitialOffsetX;
    private DispatcherTimer? _previewTimer;
    private (int Start, int End) _previewRange;
    private (CancellationToken Token, HashSet<int> Indices)? _loadingMediaSlots;

    internal readonly record struct MediaPreviewMapping(
        MediaTime SourceOffsetDelta, MediaTime StartDelta = default, MediaTime DurationDelta = default);

    private bool CanGenerateThumbnails => !_isDisposed && _timelineViewAttached
        && !IsThumbnailsDisabled.Value && GetEffectiveThumbnailWidth() >= MinimumThumbnailWidth;

    private double GetEffectiveThumbnailWidth()
        => _holdPreviewUntilThumbnailsRefresh
            ? Model.Length.Value.TimeToPixel(Timeline.Options.Value.Scale)
            : Width.Value;

    internal void BeginMediaPreview()
    {
        if (_isDisposed || _isEditPreviewActive) return;

        _isEditPreviewActive = true;
        _previewInitialOffsetX = ThumbnailContentOffsetX.Value;
        _thumbnailRefreshPending |= _thumbnailsCts != null || _scrollThumbnailsCts != null;
        CancelThumbnailsLoading();
        CancelScrollThumbnailsLoading();

        if (_holdPreviewUntilThumbnailsRefresh)
        {
            IsThumbnailPreviewActive.Value = !double.IsNaN(ThumbnailContentWidth.Value);
            QueueMediaPreviewForViewport();
            return;
        }

        double contentWidth = GetMediaViewportWidth?.Invoke() ?? 0;
        if (contentWidth <= 0)
            contentWidth = Math.Max(0, Width.Value - 2);

        if ((VideoThumbnailCount.Value > 0 || WaveformChunkCount.Value > 0) && contentWidth > 0)
        {
            _previewLayoutDuration = (_currentThumbnailsProvider as EngineObject)?.TimeRange.Value.Duration ?? Model.Length.Value;
            ThumbnailContentWidth.Value = contentWidth;
            ThumbnailContentOffsetX.Value = 0;
            IsThumbnailPreviewActive.Value = true;
            QueueMediaPreviewForViewport();
        }
    }

    internal void UpdateMediaPreview(double contentOffsetX, MediaPreviewMapping? mapping = null)
    {
        if (_isDisposed) return;

        BeginMediaPreview();
        if (!double.IsNaN(ThumbnailContentWidth.Value))
        {
            ThumbnailContentOffsetX.Value = _previewInitialOffsetX + (mapping.HasValue ? 0 : contentOffsetX);
        }

        if (!IsThumbnailPreviewActive.Value || _previewMapping == mapping) return;

        _previewMapping = mapping;
        QueueMediaPreviewForViewport();
    }

    private void QueueMediaPreviewForViewport()
    {
        var range = _currentThumbnailsProvider switch
        {
            Beutl.Graphics.SourceVideo => GetVisibleThumbnailRange?.Invoke(),
            Beutl.Audio.SourceSound => GetVisibleWaveformRange?.Invoke(),
            _ => null
        };
        if (range is { } visible)
            QueueMediaPreview(visible.Start, visible.End);
    }

    private void QueueMediaPreview(int start, int end)
    {
        if (_isDisposed || !IsThumbnailPreviewActive.Value || end < start) return;

        _previewRange = (start, end);
        if (_previewTimer == null)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher.UIThread)
            {
                Interval = TimeSpan.FromMilliseconds(30)
            };
            MediaPreviewMapping? waitingMapping = null;
            timer.Tick += (_, _) =>
            {
                if (_scrollThumbnailsCts != null)
                {
                    MediaPreviewMapping? requested = _previewMapping;
                    // Let a decode make progress during continuous input. A settled request can supersede it.
                    if (requested == _decodingPreviewMapping || requested != waitingMapping)
                    {
                        waitingMapping = requested;
                        return;
                    }

                    CancelScrollThumbnailsLoading();
                }

                waitingMapping = null;
                timer.Stop();
                _ = UpdatePreviewMediaAsync(_previewRange.Start, _previewRange.End);
            };
            _previewTimer = timer;
        }

        // Keep the original deadline while pointer events replace the queued range.
        if (!_previewTimer.IsEnabled)
            _previewTimer.Start();
    }

    internal void EndResizeMediaPreview(MediaTime initialStart, MediaTime initialEnd)
    {
        MediaTime startDelta = Model.Start.Value - initialStart;
        bool changed = startDelta != MediaTime.Zero || Model.Range.End != initialEnd;
        MediaPreviewMapping? mapping = _previewMapping is { } preview
            ? preview with { StartDelta = startDelta, DurationDelta = Model.Length.Value - (initialEnd - initialStart) }
            : null;
        EndMediaPreview(changed, appliedMapping: mapping);
    }

    internal void EndMediaPreview(bool committed, double? finalContentOffsetX = null, MediaPreviewMapping? appliedMapping = null)
    {
        if (_isDisposed || !_isEditPreviewActive) return;

        bool resampled = _previewMapping.HasValue || appliedMapping.HasValue;
        _isEditPreviewActive = false;
        _previewMapping = null;
        _previewTimer?.Stop();
        CancelScrollThumbnailsLoading();

        if (committed || _holdPreviewUntilThumbnailsRefresh)
        {
            if (committed && (_displayedPreviewMapping == null || _displayedPreviewMapping == appliedMapping))
            {
                _displayedPreviewMapping = null;
                MediaPreviewCommitted?.Invoke();
            }
            else
            {
                ClearDisplayedPreview();
            }

            IsThumbnailPreviewActive.Value = false;
            if (!committed)
                ThumbnailContentOffsetX.Value = _previewInitialOffsetX;
            else if (finalContentOffsetX is { } finalOffset && !double.IsNaN(ThumbnailContentWidth.Value))
                ThumbnailContentOffsetX.Value = _previewInitialOffsetX + (resampled ? 0 : finalOffset);

            _holdPreviewUntilThumbnailsRefresh = true;
            Interlocked.Increment(ref _thumbnailLayoutVersion);
            UpdateThumbnailsAsync();
            return;
        }

        ResetMediaPreviewVisuals();
        if (_thumbnailRefreshPending)
            UpdateThumbnailsAsync();
    }

    private void ClearDisplayedPreview()
    {
        _displayedPreviewMapping = null;
        MediaPreviewClear?.Invoke();
    }

    private void ResetMediaPreviewVisuals()
    {
        _holdPreviewUntilThumbnailsRefresh = false;
        if (_isDisposed) return;

        ClearDisplayedPreview();
        IsThumbnailPreviewActive.Value = false;
        ThumbnailContentWidth.Value = double.NaN;
        ThumbnailContentOffsetX.Value = 0;
    }

    internal void SetTimelineViewAttached(bool attached)
    {
        _timelineViewAttached = attached;
        if (attached)
        {
            UpdateThumbnailsAsync();
        }
        else
        {
            ClearVisibleThumbnails();
        }

        OnAnimationMarksViewAttached(attached);
    }

    private bool ShowThumbnailsByDefault
        => Timeline.EditorContext.GetRequiredService<GlobalConfiguration>().EditorConfig.ShowTimelineThumbnailsByDefault.Value;

    // A clip's own choice is kept only where it differs from the default: in ThumbnailsDisabledElements
    // while thumbnails are shown by default, and in ThumbnailsEnabledElements while they are not.
    private bool ComputeIsThumbnailsDisabled()
    {
        Guid id = Model.Id.Value;
        return ShowThumbnailsByDefault
            ? Timeline.ThumbnailsDisabledElements.Contains(id)
            : !Timeline.ThumbnailsEnabledElements.Contains(id);
    }

    // Set while the value follows the lists or the default, so only a user's change is written back to them.
    private bool _refreshingThumbnailsDisabled;

    internal void RefreshIsThumbnailsDisabled()
    {
        bool isDisabled = ComputeIsThumbnailsDisabled();
        if (IsThumbnailsDisabled.Value != isDisabled)
        {
            _refreshingThumbnailsDisabled = true;
            try
            {
                IsThumbnailsDisabled.Value = isDisabled;
            }
            finally
            {
                _refreshingThumbnailsDisabled = false;
            }
        }
    }

    private static void SetMembership(CoreList<Guid> list, Guid id, bool member)
    {
        if (member)
        {
            if (!list.Contains(id)) list.Add(id);
        }
        else
        {
            list.Remove(id);
        }
    }

    private void InitializeThumbnails()
    {
        IsThumbnailsDisabled.Value = ComputeIsThumbnailsDisabled();

        IsThumbnailsDisabled.Skip(1)
            .Subscribe(isDisabled =>
            {
                if (!_refreshingThumbnailsDisabled)
                {
                    // The newer choice replaces one made under the other default, so a later flip cannot revive it.
                    // The current default's list goes first: each change refreshes this value, which must already agree.
                    Guid id = Model.Id.Value;
                    if (ShowThumbnailsByDefault)
                    {
                        SetMembership(Timeline.ThumbnailsDisabledElements, id, isDisabled);
                        Timeline.ThumbnailsEnabledElements.Remove(id);
                    }
                    else
                    {
                        SetMembership(Timeline.ThumbnailsEnabledElements, id, !isDisabled);
                        Timeline.ThumbnailsDisabledElements.Remove(id);
                    }
                }

                UpdateThumbnailsAsync();
            })
            .AddTo(_disposables);

        // Stop pending work as soon as the clip becomes narrow; only regeneration is debounced.
        Width.Select(width => width < MinimumThumbnailWidth)
            .DistinctUntilChanged()
            .Where(narrow => narrow && _timelineViewAttached && !_isEditPreviewActive)
            .ObserveOnUIDispatcher()
            .Subscribe(_ =>
            {
                if (!_isEditPreviewActive && GetEffectiveThumbnailWidth() < MinimumThumbnailWidth) UpdateThumbnailsAsync();
            })
            .AddTo(_disposables);

        Observable.Merge(
                Timeline.Scale.Skip(1).Select(_ => Unit.Default),
                Timeline.LayerRowHeight.Skip(1).Select(_ => Unit.Default))
            .Do(_ =>
            {
                // Keep the pending layout change across the end of the pointer gesture.
                if (_timelineViewAttached && _isEditPreviewActive)
                {
                    Interlocked.Increment(ref _thumbnailLayoutVersion);
                    _thumbnailRefreshPending = true;
                }
            })
            .ObserveOnUIDispatcher()
            .Subscribe(_ =>
            {
                if (_holdPreviewUntilThumbnailsRefresh && !_isEditPreviewActive)
                    ResetMediaPreviewVisuals();
            })
            .AddTo(_disposables);

        // Changing clip width or row height changes the video slot count, so cached slot indices no longer match.
        // Advance _thumbnailLayoutVersion so queued old-count ranges and in-flight old-geometry loads cannot
        // cancel the rebuild or clear the stale state before the new layout publishes its count.
        Observable.Merge(
                Width.Select(_ => true),
                Timeline.LayerRowHeight.Skip(1)
                    .Where(_ => ThumbnailsKind.Value == Engine.ThumbnailsKind.Video)
                    .Select(_ => true),
                _thumbnailsInvalidatedSubject.AsObservable().Select(_ => false))
            .Where(_ => _timelineViewAttached && !_isEditPreviewActive)
            .Do(resetVisibleRange =>
            {
                if (resetVisibleRange) Interlocked.Increment(ref _thumbnailLayoutVersion);
            })
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOnUIDispatcher()
            .Subscribe(_ =>
            {
                UpdateThumbnailsAsync();
            })
            .AddTo(_disposables);

        _visibleRangeSubject
            .Where(_ => !IsThumbnailsDisabled.Value && !_isEditPreviewActive)
            .Throttle(TimeSpan.FromMilliseconds(50))
            .ObserveOnUIDispatcher()
            .Subscribe(range => _ = UpdateVisibleThumbnailsAsync(range.Start, range.End))
            .AddTo(_disposables);
    }

    // The SourceVideo helper only busts the suffixed strip keys (baseKey|original / baseKey|proxy:*),
    // but SourceSound stores its waveform min/max under the bare baseKey, so an audio source/effect edit
    // would otherwise keep serving a stale waveform. Invalidate both; the bare key is a no-op for video.
    internal static void InvalidateAllThumbnailCacheKeys(IThumbnailCacheService cacheService, string? baseKey)
    {
        Beutl.Graphics.SourceVideo.InvalidateThumbnailCacheKeys(cacheService, baseKey);
        if (baseKey != null)
            cacheService.Invalidate(baseKey);
    }

    internal void InvalidateAndUpdateThumbnails()
    {
        InvalidateAllThumbnailCacheKeys(_thumbnailCacheService, _lastThumbnailsCacheKey);
        UpdateThumbnailsAsync();
    }

    internal static bool ShouldRefreshThumbnailsForDefaultPresetChange(PreviewSourceMode previewSourceMode)
        => previewSourceMode == PreviewSourceMode.PreferProxy;

    private void CancelThumbnailsLoading() => CancelAndClear(ref _thumbnailsCts);

    private void CancelScrollThumbnailsLoading() => CancelAndClear(ref _scrollThumbnailsCts);

    private static void CancelAndClear(ref CancellationTokenSource? cts)
    {
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    // Drops a request's token source unless a newer request has replaced it.
    private static void ReleaseIfCurrent(ref CancellationTokenSource? current, CancellationToken token)
    {
        if (current is { } cts && cts.Token == token)
        {
            current = null;
            cts.Dispose();
        }
    }

    private async void UpdateThumbnailsAsync()
    {
        if (_isDisposed || !_timelineViewAttached) return;
        if (_isEditPreviewActive)
        {
            _thumbnailRefreshPending = true;
            return;
        }

        _thumbnailRefreshPending = false;

        if (!CanGenerateThumbnails)
        {
            ClearVisibleThumbnails();
            return;
        }

        var provider = FindThumbnailsProvider();

        if (_currentThumbnailsProvider != provider)
        {
            SwitchThumbnailsProvider(provider);
        }

        if (provider == null)
        {
            ResetMediaPreviewVisuals();
            ThumbnailsKind.Value = Engine.ThumbnailsKind.None;
            return;
        }

        ThumbnailsKind.Value = provider.ThumbnailsKind;

        CancelThumbnailsLoading();
        _thumbnailsCts = new CancellationTokenSource();
        var ct = _thumbnailsCts.Token;

        try
        {
            switch (provider.ThumbnailsKind)
            {
                case Engine.ThumbnailsKind.Video:
                    await UpdateVideoThumbnailsAsync(provider, ct);
                    break;
                case Engine.ThumbnailsKind.Audio:
                    await UpdateAudioThumbnailsAsync(provider, ct);
                    break;
                default:
                    ResetMediaPreviewVisuals();
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update thumbnails.");
            if (!ct.IsCancellationRequested && !_isEditPreviewActive)
                ResetMediaPreviewVisuals();
        }
        finally
        {
            ReleaseIfCurrent(ref _thumbnailsCts, ct);
        }
    }

    private void ClearVisibleThumbnails()
    {
        CancelThumbnailsLoading();
        CancelScrollThumbnailsLoading();
        _previewTimer?.Stop();
        _isEditPreviewActive = false;
        _previewMapping = null;
        ResetMediaPreviewVisuals();
        Volatile.Write(ref _thumbnailCountVersion, Volatile.Read(ref _thumbnailLayoutVersion));
        Volatile.Write(ref _failedThumbnailLayoutVersion, -1);
        _lastVisibleStart = _lastVisibleEnd = -1;
        ThumbnailsKind.Value = Engine.ThumbnailsKind.None;
        VideoThumbnailCount.Value = 0;
        WaveformChunkCount.Value = 0;
        ThumbnailsClear?.Invoke();
        WaveformClear?.Invoke();
    }

    private void SwitchThumbnailsProvider(IThumbnailsProvider? provider)
    {
        if (_currentThumbnailsProvider != null && _thumbnailsInvalidatedHandler != null)
        {
            _currentThumbnailsProvider.ThumbnailsInvalidated -= _thumbnailsInvalidatedHandler;
        }

        _currentThumbnailsProvider = provider;
        _lastThumbnailsCacheKey = provider is Beutl.Graphics.SourceVideo ? null : provider?.GetThumbnailsCacheKey();

        if (provider != null)
        {
            _thumbnailsInvalidatedHandler = (_, _) =>
            {
                var oldKey = _lastThumbnailsCacheKey;
                if (oldKey != null)
                    InvalidateAllThumbnailCacheKeys(_thumbnailCacheService, oldKey);

                _lastThumbnailsCacheKey = provider is Beutl.Graphics.SourceVideo ? null : provider.GetThumbnailsCacheKey();

                if (_isEditPreviewActive)
                    _thumbnailRefreshPending = true;
                else
                    _thumbnailsInvalidatedSubject.OnNext(Unit.Default);
            };
            provider.ThumbnailsInvalidated += _thumbnailsInvalidatedHandler;
        }
    }

    private IThumbnailsProvider? FindThumbnailsProvider()
    {
        foreach (var child in Model.Objects)
        {
            if (child is IThumbnailsProvider provider)
                return provider;
        }

        return null;
    }

    internal bool RequiresResampledRangePreview(bool startChanges)
        => FindThumbnailsProvider() switch
        {
            Beutl.Graphics.SourceVideo video => video.RequiresResampledRangePreview(startChanges),
            Beutl.Audio.SourceSound sound => sound.RequiresResampledRangePreview(startChanges),
            _ => false
        };

    internal SlippableMedia.Target? FindResampledThumbnailTarget(IReadOnlyList<SlippableMedia.Target> targets, bool trim)
    {
        var provider = FindThumbnailsProvider();
        bool resample = provider switch
        {
            Beutl.Graphics.SourceVideo video => trim ? video.RequiresResampledTrimPreview : video.RequiresResampledSlipPreview,
            Beutl.Audio.SourceSound sound => trim ? sound.RequiresResampledTrimPreview : sound.RequiresResampledSlipPreview,
            _ => false
        };
        return resample && provider is ITimeMappedSource source
            ? targets.FirstOrDefault(t => ReferenceEquals(t.Offset, source.OffsetPosition))
            : null;
    }

    // Slot geometry uses the row height, while the decoder uses a bucketed render height.
    private int ThumbnailHeight => Math.Max(1, (int)Math.Round(Timeline.LayerRowHeight.Value));

    private bool PreferProxyForThumbnails => Timeline.EditorContext.GetRequiredService<GlobalConfiguration>().EditorConfig.PreviewSourceMode.Value == PreviewSourceMode.PreferProxy;

    private ProxyPreset PreferredProxyPresetForThumbnails => ToProxyPreset(Timeline.EditorContext.GetRequiredService<GlobalConfiguration>().ProxyStoreConfig.DefaultPreset.Value);

    private static ProxyPreset ToProxyPreset(int value)
    {
        return Enum.IsDefined(typeof(ProxyPreset), value)
            ? (ProxyPreset)value
            : ProxyPreset.Quarter;
    }

    private void OnProxyStoreChangedForThumbnails(ProxyStoreChangedEventArgs e)
    {
        // The reader's resolved proxy identity and version select the new cache entries.
        if (_isDisposed || !PreferProxyForThumbnails || !AffectsProxyBadge(e.Kind))
        {
            return;
        }

        OnUIThreadIfRelevant(GetChangedSourceKey(e), UpdateThumbnailsAsync);
    }

    public void OnVisibleRangeChanged(int start, int end)
    {
        if (_isEditPreviewActive)
        {
            if (_currentThumbnailsProvider is Beutl.Graphics.SourceVideo)
                QueueMediaPreview(start, end);
            return;
        }

        _lastVisibleStart = start;
        _lastVisibleEnd = end;
        _visibleRangeSubject.OnNext((start, end));
    }

    public void OnWaveformVisibleRangeChanged(int start, int end)
    {
        if (_currentThumbnailsProvider is not Beutl.Audio.SourceSound)
            return;

        if (_isEditPreviewActive)
            QueueMediaPreview(start, end);
        else
            _visibleRangeSubject.OnNext((start, end));
    }

    private async Task UpdatePreviewMediaAsync(int start, int end)
    {
        if (_isDisposed || !_timelineViewAttached || IsThumbnailsDisabled.Value || !IsThumbnailPreviewActive.Value)
            return;

        var provider = _currentThumbnailsProvider;
        var (count, getMissing) = provider switch
        {
            Beutl.Graphics.SourceVideo => (VideoThumbnailCount.Value, GetMissingThumbnailIndices),
            Beutl.Audio.SourceSound => (WaveformChunkCount.Value, GetMissingWaveformIndices),
            _ => (0, null)
        };
        if (count <= 0)
            return;

        MediaPreviewMapping? mapping = _previewMapping;
        MediaPreviewMapping parameters = mapping.GetValueOrDefault();
        // A new drag can interrupt a refresh. Keep the old slot spacing and sample the current model inside it.
        TimeRange layout = new(_previewInitialOffsetX.PixelToTime(Timeline.Options.Value.Scale), _previewLayoutDuration);
        IReadOnlyList<int>? indices = _holdPreviewUntilThumbnailsRefresh || mapping.HasValue
            ? CreateRangeIndices(_holdPreviewUntilThumbnailsRefresh ? start : Math.Max(0, start), end)
            : getMissing?.Invoke(start, end);
        if (indices is not { Count: > 0 })
            return;

        _decodingPreviewMapping = mapping;
        _scrollThumbnailsCts = new CancellationTokenSource();
        var ct = _scrollThumbnailsCts.Token;

        try
        {
            var decoders = ExtensionGuard.FindService<DecoderRegistry>(Timeline.EditorContext);
            if (provider is Beutl.Graphics.SourceVideo video)
            {
                int height = ThumbnailHeight;
                var stream = video.GetPreviewThumbnailLeasesAsync(
                    Timeline.EditorContext.GetRequiredService<FontManager>(),
                    count, height, indices, _thumbnailCacheService, ct, PreferProxyForThumbnails,
                    PreferredProxyPresetForThumbnails, decoders, parameters.SourceOffsetDelta, layout,
                    parameters.StartDelta, parameters.DurationDelta);
                await PublishBatchesAsync(stream, 4, ct, batch =>
                {
                    if (PrepareToPublish())
                        PublishVideoBatch(batch);
                }, static item => item.Thumbnail?.Dispose());
            }
            else if (provider is Beutl.Audio.SourceSound sound)
            {
                var stream = sound.GetPreviewWaveformChunksAsync(count, MaxWaveformSamplesPerChunk, indices,
                    ct, decoders, parameters.SourceOffsetDelta, layout, parameters.StartDelta, parameters.DurationDelta);
                await PublishBatchesAsync(stream, 32, ct, batch =>
                {
                    if (PrepareToPublish())
                        PublishWaveformBatch(batch);
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update media preview.");
        }
        finally
        {
            ReleaseIfCurrent(ref _scrollThumbnailsCts, ct);
        }

        bool PrepareToPublish()
        {
            if (ct.IsCancellationRequested) return false;

            if (_displayedPreviewMapping != mapping)
            {
                ClearDisplayedPreview();
                _displayedPreviewMapping = mapping;
            }

            return true;
        }
    }

    private static List<int> CreateRangeIndices(int start, int end)
    {
        if (end < start)
            return [];

        var list = new List<int>(end - start + 1);
        for (int i = start; i <= end; i++)
            list.Add(i);
        return list;
    }

    private async Task UpdateVisibleThumbnailsAsync(int start, int end)
    {
        if (!CanGenerateThumbnails || _isEditPreviewActive || _holdPreviewUntilThumbnailsRefresh || end < start)
            return;
        var provider = _currentThumbnailsProvider;
        if (provider == null)
            return;

        int layoutVersion = Volatile.Read(ref _thumbnailLayoutVersion);
        if (Volatile.Read(ref _thumbnailCountVersion) != layoutVersion)
        {
            if (_thumbnailsCts == null && Volatile.Read(ref _failedThumbnailLayoutVersion) == layoutVersion)
                UpdateThumbnailsAsync();
            return;
        }

        var (count, getMissing) = provider.ThumbnailsKind switch
        {
            Engine.ThumbnailsKind.Video => (VideoThumbnailCount.Value, GetMissingThumbnailIndices),
            Engine.ThumbnailsKind.Audio when provider is Beutl.Audio.SourceSound => (WaveformChunkCount.Value, GetMissingWaveformIndices),
            _ => (0, null)
        };
        start = Math.Max(start, 0);
        end = Math.Min(end, count - 1);
        if (end < start)
            return;

        var missing = getMissing?.Invoke(start, end);
        if (missing is not { Count: > 0 })
            return;

        if (_loadingMediaSlots is { } pending
            && (pending.Token == _thumbnailsCts?.Token || pending.Token == _scrollThumbnailsCts?.Token)
            && missing.All(pending.Indices.Contains))
            return;

        CancelThumbnailsLoading();
        CancelScrollThumbnailsLoading();
        _scrollThumbnailsCts = new CancellationTokenSource();
        var ct = _scrollThumbnailsCts.Token;
        _loadingMediaSlots = (ct, missing.ToHashSet());

        try
        {
            if (provider.ThumbnailsKind == Engine.ThumbnailsKind.Video)
                await StreamVideoThumbnailsAsync(provider, (int)GetEffectiveThumbnailWidth(), layoutVersion, ct, missing);
            else
                await StreamWaveformAsync(provider, count, layoutVersion, ct, missing);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update visible thumbnails.");
        }
        finally
        {
            ForgetLoadingSlots(ct);
            ReleaseIfCurrent(ref _scrollThumbnailsCts, ct);
        }
    }

    private async Task UpdateVideoThumbnailsAsync(IThumbnailsProvider provider, CancellationToken ct)
    {
        int layoutVersion = Volatile.Read(ref _thumbnailLayoutVersion);
        Volatile.Write(ref _failedThumbnailLayoutVersion, -1);
        CancelScrollThumbnailsLoading();
        try
        {
            await StreamVideoThumbnailsAsync(provider, (int)GetEffectiveThumbnailWidth(), layoutVersion, ct);
        }
        finally
        {
            ForgetLoadingSlots(ct);
            if (!ct.IsCancellationRequested
                && Volatile.Read(ref _thumbnailCountVersion) != layoutVersion
                && Volatile.Read(ref _thumbnailLayoutVersion) == layoutVersion)
            {
                Volatile.Write(ref _failedThumbnailLayoutVersion, layoutVersion);
            }
        }
    }

    private async Task StreamVideoThumbnailsAsync(IThumbnailsProvider provider, int width, int layoutVersion,
        CancellationToken ct, IReadOnlyList<int>? missing = null)
    {
        ct.ThrowIfCancellationRequested();
        var fonts = Timeline.EditorContext.GetRequiredService<FontManager>();
        var decoders = ExtensionGuard.FindService<DecoderRegistry>(Timeline.EditorContext);
        int height = ThumbnailHeight;
        bool replace = missing == null;
        int newCount = 0;
        var stream = provider is Beutl.Graphics.SourceVideo video
            ? video.GetVisibleThumbnailLeasesAsync(fonts, width, height, _thumbnailCacheService, ct,
                PreferProxyForThumbnails, PreferredProxyPresetForThumbnails, decoders, SelectIndicesAsync)
            : GetLegacyVideoThumbnailsAsync(provider, fonts, decoders, width, height, missing, ct);

        await PublishBatchesAsync(stream, 4, ct, batch =>
        {
            if (!CanPublish(layoutVersion, ct))
                return;

            ApplyLayout(batch[0].Count);
            PublishVideoBatch(batch);
        }, static item => item.Thumbnail?.Dispose());

        if (CanPublish(layoutVersion, ct))
            ApplyLayout(newCount);

        void ApplyLayout(int count)
        {
            if (!replace)
                return;
            replace = false;
            ResetMediaPreviewVisuals();
            ThumbnailsClear?.Invoke();
            Volatile.Write(ref _thumbnailCountVersion, layoutVersion);
            VideoThumbnailCount.Value = count;
        }

        async ValueTask<IReadOnlyList<int>> SelectIndicesAsync(int count)
        {
            if (missing != null)
                return missing;

            return await Dispatcher.UIThread.InvokeAsync<IReadOnlyList<int>>(() =>
            {
                if (!CanPublish(layoutVersion, ct))
                    return [];

                newCount = count;
                var (start, end) = GetVisibleThumbnailRangeForLayout?.Invoke(count, width) ?? (0, -1);
                var indices = CreateRangeIndices(start, end);
                _loadingMediaSlots = (ct, indices.ToHashSet());
                return indices;
            });
        }
    }

    private async IAsyncEnumerable<(int Index, int Count, Ref<Bitmap>? Thumbnail)> GetLegacyVideoThumbnailsAsync(
        IThumbnailsProvider provider, FontManager fonts, DecoderRegistry? decoders, int width, int height,
        IReadOnlyList<int>? indices, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var (start, end) in GetRanges())
        {
            await foreach (var (index, count, thumbnail) in provider.GetThumbnailStripAsync(
                               fonts, width, height, _thumbnailCacheService, ct, start, end, decoders))
                yield return (index, count, Ref<Bitmap>.Create(thumbnail));
        }

        IEnumerable<(int Start, int End)> GetRanges()
        {
            if (indices == null)
            {
                bool currentLayout = _thumbnailCountVersion == _thumbnailLayoutVersion;
                yield return currentLayout && _lastVisibleStart >= 0 && _lastVisibleEnd >= _lastVisibleStart
                    ? (_lastVisibleStart, _lastVisibleEnd) : (0, -1);
                yield break;
            }

            int start = indices[0];
            int end = start;
            for (int i = 1; i < indices.Count; i++)
            {
                if (indices[i] != end + 1)
                {
                    yield return (start, end);
                    start = indices[i];
                }
                end = indices[i];
            }
            yield return (start, end);
        }
    }

    private async Task UpdateAudioThumbnailsAsync(IThumbnailsProvider provider, CancellationToken ct)
    {
        double width = GetEffectiveThumbnailWidth();
        int count = Math.Max(1, (int)width);
        int layoutVersion = Volatile.Read(ref _thumbnailLayoutVersion);
        CancelScrollThumbnailsLoading();

        IReadOnlyList<int>? indices = null;
        if (provider is Beutl.Audio.SourceSound)
        {
            var (start, end) = GetVisibleWaveformRangeForLayout?.Invoke(count, width) ?? (0, -1);
            indices = CreateRangeIndices(start, end);
            _loadingMediaSlots = (ct, indices.ToHashSet());
        }

        try
        {
            await StreamWaveformAsync(provider, count, layoutVersion, ct, indices, replace: true);
        }
        finally
        {
            ForgetLoadingSlots(ct);
        }
    }

    private async Task StreamWaveformAsync(IThumbnailsProvider provider, int count,
        int layoutVersion, CancellationToken ct, IReadOnlyList<int>? missing, bool replace = false)
    {
        var decoders = ExtensionGuard.FindService<DecoderRegistry>(Timeline.EditorContext);
        var stream = provider is Beutl.Audio.SourceSound sound
            ? sound.GetWaveformChunksAsync(count, MaxWaveformSamplesPerChunk, _thumbnailCacheService, missing, ct, decoders)
            : provider.GetWaveformChunksAsync(count, MaxWaveformSamplesPerChunk, _thumbnailCacheService, ct, decoders);
        try
        {
            await PublishBatchesAsync(stream, 32, ct, batch =>
            {
                if (!CanPublish(layoutVersion, ct))
                    return;
                ApplyLayout();
                PublishWaveformBatch(batch);
            });
        }
        finally
        {
            if (CanPublish(layoutVersion, ct))
                ApplyLayout();
        }

        void ApplyLayout()
        {
            if (!replace)
                return;
            replace = false;
            ResetMediaPreviewVisuals();
            WaveformClear?.Invoke();
            Volatile.Write(ref _thumbnailCountVersion, layoutVersion);
            WaveformChunkCount.Value = count;
        }
    }

    private bool CanPublish(int layoutVersion, CancellationToken ct)
        => !ct.IsCancellationRequested && !_isEditPreviewActive && layoutVersion == Volatile.Read(ref _thumbnailLayoutVersion);

    private void ForgetLoadingSlots(CancellationToken ct)
    {
        if (_loadingMediaSlots?.Token == ct)
            _loadingMediaSlots = null;
    }

    private void PublishVideoBatch(IReadOnlyList<(int Index, int Count, Ref<Bitmap>? Thumbnail)> batch)
    {
        if (ThumbnailBatchReady == null && ThumbnailReady == null)
            return;

        var images = new (int Index, WriteableBitmap? Thumbnail)[batch.Count];
        try
        {
            for (int i = 0; i < batch.Count; i++)
                images[i] = (batch[i].Index, batch[i].Thumbnail?.Value.ToAvaWriteableBitmap());
        }
        catch
        {
            foreach (var (_, thumbnail) in images)
                thumbnail?.Dispose();
            throw;
        }

        ThumbnailBatchReady?.Invoke(images);
        if (ThumbnailReady is { } ready)
        {
            foreach (var (index, thumbnail) in images)
                ready(index, thumbnail);
        }
    }

    private void PublishWaveformBatch(IReadOnlyList<WaveformChunk> batch)
    {
        WaveformBatchReady?.Invoke(batch);
        if (WaveformChunkReady is { } ready)
        {
            foreach (var chunk in batch)
                ready(chunk);
        }
    }

    private static async Task PublishBatchesAsync<T>(IAsyncEnumerable<T> source, int size, CancellationToken ct,
        Action<IReadOnlyList<T>> publish, Action<T>? release = null)
    {
        var batch = new List<T>(size);
        long lastPublished = Stopwatch.GetTimestamp();
        try
        {
            await foreach (var item in source.ConfigureAwait(false))
            {
                batch.Add(item);
                ct.ThrowIfCancellationRequested();
                if (batch.Count == size || Stopwatch.GetElapsedTime(lastPublished).TotalMilliseconds >= 40)
                    await PublishAsync().ConfigureAwait(false);
            }

            if (batch.Count > 0)
                await PublishAsync().ConfigureAwait(false);
        }
        finally
        {
            ReleaseBatch();
        }

        async Task PublishAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                    publish(batch);
            });
            ReleaseBatch();
            lastPublished = Stopwatch.GetTimestamp();
        }

        void ReleaseBatch()
        {
            if (release != null)
            {
                foreach (var item in batch)
                    release(item);
            }
            batch.Clear();
        }
    }
}
