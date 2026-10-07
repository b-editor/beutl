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
    private void InitializeThumbnails()
    {
        // プレビュー無効化の初期値を設定
        IsThumbnailsDisabled.Value = Timeline.ThumbnailsDisabledElements.Contains(Model.Id);

        // ThumbnailsDisabledElementsの変更を購読
        Timeline.ThumbnailsDisabledElements.Attached += OnThumbnailsDisabledElementsAttached;
        Timeline.ThumbnailsDisabledElements.Detached += OnThumbnailsDisabledElementsDetached;

        // IsThumbnailsDisabledが変更されたらThumbnailsDisabledElementsを更新し、プレビューを再読み込み
        IsThumbnailsDisabled.Skip(1)
            .Subscribe(isDisabled =>
            {
                if (isDisabled)
                {
                    if (!Timeline.ThumbnailsDisabledElements.Contains(Model.Id))
                    {
                        Timeline.ThumbnailsDisabledElements.Add(Model.Id);
                    }
                }
                else
                {
                    Timeline.ThumbnailsDisabledElements.Remove(Model.Id);
                }

                UpdateThumbnailsAsync();
            })
            .AddTo(_disposables);

        // Width変更とThumbnailsInvalidatedイベントをマージして、いずれかが発生してから500ms後に更新
        Observable.Merge(
                Width.Select(_ => Unit.Default),
                _thumbnailsInvalidatedSubject.AsObservable())
            .Throttle(TimeSpan.FromMilliseconds(500))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => UpdateThumbnailsAsync())
            .AddTo(_disposables);

        // スクロール時の可視範囲変更をスロットルして追加生成
        _visibleRangeSubject
            .Where(_ => !IsThumbnailsDisabled.Value)
            .Throttle(TimeSpan.FromMilliseconds(50))
            .ObserveOnUIDispatcher()
            .Subscribe(range => _ = UpdateVisibleThumbnailsAsync(range.Start, range.End))
            .AddTo(_disposables);

        // Switching PreviewSourceMode changes the decode path (proxy vs original), so an already
        // rendered filmstrip is stale until an unrelated invalidation; drop its cache and re-render.
        GlobalConfiguration.Instance.EditorConfig.GetObservable(EditorConfig.PreviewSourceModeProperty)
            .Skip(1)
            .DistinctUntilChanged()
            .ObserveOnUIDispatcher()
            .Subscribe(_ => InvalidateAndUpdateThumbnails())
            .AddTo(_disposables);

        GlobalConfiguration.Instance.ProxyStoreConfig.GetObservable(ProxyStoreConfig.DefaultPresetProperty)
            .Skip(1)
            .DistinctUntilChanged()
            .Where(_ => ShouldRefreshThumbnailsForDefaultPresetChange(GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode))
            .ObserveOnUIDispatcher()
            .Subscribe(_ => InvalidateAndUpdateThumbnails())
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

    private void InvalidateAndUpdateThumbnails()
    {
        InvalidateAllThumbnailCacheKeys(_thumbnailCacheService, _lastThumbnailsCacheKey);
        UpdateThumbnailsAsync();
    }

    internal static bool ShouldRefreshThumbnailsForDefaultPresetChange(PreviewSourceMode previewSourceMode)
        => previewSourceMode == PreviewSourceMode.PreferProxy;

    private void OnThumbnailsDisabledElementsAttached(Guid id)
    {
        if (id == Model.Id && !IsThumbnailsDisabled.Value)
        {
            IsThumbnailsDisabled.Value = true;
        }
    }

    private void OnThumbnailsDisabledElementsDetached(Guid id)
    {
        if (id == Model.Id && IsThumbnailsDisabled.Value)
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
        // プレビューが無効化されている場合は何もしない
        if (IsThumbnailsDisabled.Value)
        {
            CancelThumbnailsLoading();
            ThumbnailsKind.Value = Engine.ThumbnailsKind.None;
            ThumbnailsClear?.Invoke();
            WaveformClear?.Invoke();
            return;
        }

        var provider = FindThumbnailsProvider();

        // プロバイダーが変更された場合、イベント購読を更新
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
            // 生成完了後、このCTSがまだ現役なら解放する
            if (_thumbnailsCts is { } cts && cts.Token == ct)
            {
                _thumbnailsCts = null;
                cts.Dispose();
            }
        }
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

                // 新しいキーをキャプチャ
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

    private bool PreferProxyForThumbnails => GlobalConfiguration.Instance.EditorConfig.PreviewSourceMode == PreviewSourceMode.PreferProxy;

    private ProxyPreset PreferredProxyPresetForThumbnails => ToProxyPreset(GlobalConfiguration.Instance.ProxyStoreConfig.DefaultPreset);

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
        if (_isDisposed)
            return;

        // Filmstrip thumbnails are cached without proxy availability in the key, so a proxy
        // registered/deleted/changed for this element's source (in prefer-proxy mode) leaves the
        // strip decoded from the previous path until an unrelated edit; drop the cache and re-render.
        // A store swap (Reset) moves the whole store — the same invalidating kinds as the badge — so share
        // that gate, and (like the badge path) bypass the per-source relevance check for a Reset, which
        // carries no specific Source.
        if (!PreferProxyForThumbnails || !AffectsProxyBadge(e.Kind))
        {
            return;
        }

        if (!Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => OnProxyStoreChangedForThumbnails(e));
            return;
        }

        if (e.Kind != ProxyStoreChangeKind.Reset && !ElementUsesChangedSourceCached(e.Source.AbsolutePath))
        {
            return;
        }

        if (_lastThumbnailsCacheKey != null)
            InvalidateAllThumbnailCacheKeys(_thumbnailCacheService, _lastThumbnailsCacheKey);

        UpdateThumbnailsAsync();
    }

    public void OnVisibleRangeChanged(int start, int end)
    {
        _lastVisibleStart = start;
        _lastVisibleEnd = end;
        _visibleRangeSubject.OnNext((start, end));
    }

    private async Task UpdateVisibleThumbnailsAsync(int start, int end)
    {
        if (end < start) return;

        var provider = _currentThumbnailsProvider;
        if (provider == null || provider.ThumbnailsKind != Engine.ThumbnailsKind.Video)
            return;

        // Width変更による生成が進行中ならキャンセルする
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
            // 新しい要求に差し替えられていなければ、このCTSを解放する
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
