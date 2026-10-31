using System.Reactive;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Engine;
using Beutl.Media.Proxy;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class ElementViewModel
{
    // Below the thumbnail height, the clip is too narrow to show useful media detail.
    internal const double MinimumThumbnailWidth = 25;
    private bool _timelineViewAttached;

    private bool CanGenerateThumbnails => !_isDisposed && _timelineViewAttached
        && !IsThumbnailsDisabled.Value && Width.Value >= MinimumThumbnailWidth;

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
    }

    private void InitializeThumbnails()
    {
        IsThumbnailsDisabled.Value = Timeline.ThumbnailsDisabledElements.Contains(Model.Id.Value);

        IsThumbnailsDisabled.Skip(1)
            .Subscribe(isDisabled =>
            {
                if (isDisabled)
                {
                    if (!Timeline.ThumbnailsDisabledElements.Contains(Model.Id.Value))
                    {
                        Timeline.ThumbnailsDisabledElements.Add(Model.Id.Value);
                    }
                }
                else
                {
                    Timeline.ThumbnailsDisabledElements.Remove(Model.Id.Value);
                }

                UpdateThumbnailsAsync();
            })
            .AddTo(_disposables);

        // Stop pending work as soon as the clip becomes narrow; only regeneration is debounced.
        Width.Select(width => width < MinimumThumbnailWidth)
            .DistinctUntilChanged()
            .Where(narrow => narrow && _timelineViewAttached)
            .ObserveOnUIDispatcher()
            .Subscribe(_ =>
            {
                if (Width.Value < MinimumThumbnailWidth) UpdateThumbnailsAsync();
            })
            .AddTo(_disposables);

        Observable.Merge(
                Width.Select(_ => Unit.Default),
                _thumbnailsInvalidatedSubject.AsObservable())
            .Where(_ => _timelineViewAttached)
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => UpdateThumbnailsAsync())
            .AddTo(_disposables);

        _visibleRangeSubject
            .Where(_ => !IsThumbnailsDisabled.Value)
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

    internal void OnThumbnailsDisabledElementsAttached(Guid id)
    {
        if (id == Model.Id.Value && !IsThumbnailsDisabled.Value)
        {
            IsThumbnailsDisabled.Value = true;
        }
    }

    internal void OnThumbnailsDisabledElementsDetached(Guid id)
    {
        if (id == Model.Id.Value && IsThumbnailsDisabled.Value)
        {
            IsThumbnailsDisabled.Value = false;
        }
    }

    private void CancelThumbnailsLoading()
    {
        _thumbnailsCts?.Cancel();
        _thumbnailsCts?.Dispose();
        _thumbnailsCts = null;
    }

    private async void UpdateThumbnailsAsync()
    {
        if (_isDisposed || !_timelineViewAttached) return;

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
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update thumbnails.");
        }
        finally
        {
            if (_thumbnailsCts is { } cts && cts.Token == ct)
            {
                _thumbnailsCts = null;
                cts.Dispose();
            }
        }
    }

    private void ClearVisibleThumbnails()
    {
        CancelThumbnailsLoading();
        _scrollThumbnailsCts?.Cancel();
        _scrollThumbnailsCts?.Dispose();
        _scrollThumbnailsCts = null;
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
        _lastThumbnailsCacheKey = provider?.GetThumbnailsCacheKey();

        if (provider != null)
        {
            _thumbnailsInvalidatedHandler = (_, _) =>
            {
                var oldKey = _lastThumbnailsCacheKey;
                if (oldKey != null)
                    InvalidateAllThumbnailCacheKeys(_thumbnailCacheService, oldKey);

                _lastThumbnailsCacheKey = _currentThumbnailsProvider?.GetThumbnailsCacheKey();

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

    private bool PreferProxyForThumbnails => GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode.Value == PreviewSourceMode.PreferProxy;

    private ProxyPreset PreferredProxyPresetForThumbnails => ToProxyPreset(GlobalConfiguration.Instance.ProxyStoreConfig.DefaultPreset.Value);

    private IAsyncEnumerable<(int Index, int Count, Beutl.Media.Bitmap Thumbnail)> GetVideoThumbnailStrip(
        IThumbnailsProvider provider, int width, int height, CancellationToken ct, int start, int end)
    {
        return provider is Beutl.Graphics.SourceVideo video
            ? video.GetThumbnailStripAsync(
                width,
                height,
                _thumbnailCacheService,
                ct,
                start,
                end,
                PreferProxyForThumbnails,
                PreferredProxyPresetForThumbnails)
            : provider.GetThumbnailStripAsync(width, height, _thumbnailCacheService, ct, start, end);
    }

    private static ProxyPreset ToProxyPreset(int value)
    {
        return Enum.IsDefined(typeof(ProxyPreset), value)
            ? (ProxyPreset)value
            : ProxyPreset.Quarter;
    }

    private void OnProxyStoreChangedForThumbnails(ProxyStoreChangedEventArgs e)
    {
        // The filmstrip's cache key does not include whether a proxy exists, so a relevant store change drops
        // the cache and re-renders. A Reset skips the relevance check, as it does for the badge.
        if (_isDisposed || !PreferProxyForThumbnails || !AffectsProxyBadge(e.Kind))
        {
            return;
        }

        OnUIThreadIfRelevant(GetChangedSourceKey(e), () =>
        {
            if (_lastThumbnailsCacheKey != null)
                InvalidateAllThumbnailCacheKeys(_thumbnailCacheService, _lastThumbnailsCacheKey);

            UpdateThumbnailsAsync();
        });
    }

    public void OnVisibleRangeChanged(int start, int end)
    {
        _lastVisibleStart = start;
        _lastVisibleEnd = end;
        _visibleRangeSubject.OnNext((start, end));
    }

    private async Task UpdateVisibleThumbnailsAsync(int start, int end)
    {
        if (!CanGenerateThumbnails || end < start) return;

        var provider = _currentThumbnailsProvider;
        if (provider == null || provider.ThumbnailsKind != Engine.ThumbnailsKind.Video)
            return;

        CancelThumbnailsLoading();

        var missing = GetMissingThumbnailIndices?.Invoke(start, end);
        if (missing == null || missing.Count == 0)
            return;

        _scrollThumbnailsCts?.Cancel();
        _scrollThumbnailsCts?.Dispose();
        _scrollThumbnailsCts = new CancellationTokenSource();
        var ct = _scrollThumbnailsCts.Token;

        try
        {
            double width = Width.Value;
            if (width <= 0)
                return;

            await StreamVideoThumbnailsAsync(provider, (int)width, start, end, ct);
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
            if (_scrollThumbnailsCts is { } cts && cts.Token == ct)
            {
                _scrollThumbnailsCts = null;
                cts.Dispose();
            }
        }
    }

    private async Task UpdateVideoThumbnailsAsync(IThumbnailsProvider provider, CancellationToken ct)
    {
        double width = Width.Value;
        if (width <= 0)
            return;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ct.IsCancellationRequested)
            {
                ThumbnailsClear?.Invoke();
            }
        });

        int startIndex = _lastVisibleStart >= 0 ? _lastVisibleStart : 0;
        int endIndex = _lastVisibleEnd >= 0 ? _lastVisibleEnd : -1;

        await StreamVideoThumbnailsAsync(provider, (int)width, startIndex, endIndex, ct);
    }

    // Each thumbnail is disposed on the UI thread after the hand-off, or here once the request is cancelled.
    private async Task StreamVideoThumbnailsAsync(IThumbnailsProvider provider, int width, int start, int end, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const int MaxThumbnailHeight = 25;
        await foreach (var (index, count, thumbnail) in GetVideoThumbnailStrip(provider, width, MaxThumbnailHeight, ct, start, end))
        {
            if (ct.IsCancellationRequested)
            {
                thumbnail.Dispose();
                break;
            }

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                using (thumbnail)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        VideoThumbnailCount.Value = count;
                        ThumbnailReady?.Invoke(index, !thumbnail.IsDisposed ? thumbnail.ToAvaWriteableBitmap(null) : null);
                    }
                }
            });
        }
    }

    private async Task UpdateAudioThumbnailsAsync(IThumbnailsProvider provider, CancellationToken ct)
    {
        const int MaxSamplesPerChunk = 4096;
        double width = Width.Value;
        if (width <= 0)
            return;

        int chunkCount = Math.Max(1, (int)width);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ct.IsCancellationRequested)
            {
                WaveformClear?.Invoke();
                WaveformChunkCount.Value = chunkCount;
            }
        });

        ct.ThrowIfCancellationRequested();
        await foreach (var chunk in provider.GetWaveformChunksAsync(chunkCount, MaxSamplesPerChunk, _thumbnailCacheService, ct))
        {
            if (ct.IsCancellationRequested)
                break;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                {
                    WaveformChunkReady?.Invoke(chunk);
                }
            });
        }
    }
}
