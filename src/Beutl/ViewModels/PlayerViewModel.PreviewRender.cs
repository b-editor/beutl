using Beutl.Configuration;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using SkiaSharp;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    // Both displayed images retain their own reference; the caller still owns the supplied frames.
    private void UpdateImage(Ref<Bitmap> source, Ref<Bitmap>? cleanSource, MediaTime time, long playbackId = 0)
    {
        lock (_cleanPreviewLock)
        {
            _cleanPreviewTime = time;
            _cleanPreviewPlaybackId = playbackId;
            ReplaceImage(CleanPreviewImage, _cleanPreviewEnabled ? cleanSource : null);
        }

        ReplaceImage(PreviewImage, source);

        PreviewInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private static void ReplaceImage(ReactivePropertySlim<Ref<Bitmap>?> target, Ref<Bitmap>? source)
    {
        var previous = target.Value;
        target.Value = source?.Clone();
        previous?.Dispose();
    }

    private void ReportPreviewRenderError(CancellationToken token)
    {
        UpdatePreviewRenderError(
            MessageStrings.FrameDrawingException,
            () => !token.IsCancellationRequested);
    }

    private void ClearPreviewRenderError(CancellationToken token)
    {
        UpdatePreviewRenderError(null, () => !token.IsCancellationRequested);
    }

    private void ClearPreviewRenderError(int generation)
    {
        UpdatePreviewRenderError(null, () => _sessionGuard.Owns(generation));
    }

    internal bool ApplyPlaybackRenderFailure(
        BufferedPlayer.RenderFailure failure,
        Rational rate,
        Func<bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(isCurrent);
        if (Scene is null || !isCurrent())
            return false;

        _editorClock.Seek(MediaTime.FromFrames(failure.Frame, rate));
        EditViewModel.FrameCacheManager.Value.CurrentFrame = failure.Frame;
        UpdatePreviewRenderError(MessageStrings.FrameDrawingException, isCurrent);
        IsPlaying.Value = false;
        return true;
    }

    private void UpdatePreviewRenderError(string? message, Func<bool> isCurrent)
    {
        if (!isCurrent())
            return;

        long version = Interlocked.Increment(ref _previewRenderErrorIssuedVersion);

        void Update()
        {
            // A queued update may run after this view model was disposed or its render/playback
            // request was superseded. Keep the error scoped to the preview that still owns it.
            // Invalid newer updates do not advance the applied version, so they cannot suppress
            // an older update that is still valid for the current request.
            if (Scene is not null
                && isCurrent()
                && version > _previewRenderErrorAppliedVersion)
            {
                _previewRenderErrorAppliedVersion = version;
                _previewRenderError.Value = message;
            }
        }

        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
        {
            Update();
        }
        else
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(
                Update,
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void DrawBoundaries(Renderer renderer, SKCanvas canvas, Size canvasSize)
    {
        int? selected = _editorSelection.SelectedLayerNumber.Value;
        if (selected.HasValue)
        {
            var frameSize = new Size(renderer.FrameSize.Width, renderer.FrameSize.Height);
            var frameScale = canvasSize.Width / frameSize.Width;
            float strokeScale = Stretch.Uniform.CalculateScaling(MaxFrameSize, frameSize).X;
            if (strokeScale < 1)
                strokeScale = 1;

            Rect[] boundary = renderer.RecalculateBoundaries(selected.Value);
            if (boundary.Length > 0)
            {
                using var paint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = strokeScale
                };
                bool exactBounds = GlobalConfiguration.Instance.ViewConfig.ShowExactBoundaries.Value;

                foreach (Rect item in boundary)
                {
                    Rect rect = item;
                    if (!exactBounds)
                    {
                        rect = item.Inflate(4 / strokeScale);
                    }

                    rect *= frameScale;
                    canvas.DrawRect(rect.ToSKRect(), paint);
                }
            }
        }
    }

    // Re-render the current frame into the viewport. Used when something outside the normal edit /
    // playback path (e.g. a PreviewSourceMode switch) invalidates the shown frame while paused.
    public void QueuePreviewRender() => QueueRender();

    private void QueueRender()
    {
        CancellationTokenSource cts;
        CancellationTokenSource? previousCts;
        CancellationToken token;
        lock (_renderRequestLock)
        {
            if (_isDisposing || _isPausing)
                return;

            cts = new CancellationTokenSource();
            token = cts.Token;
            previousCts = _cts;
            _cts = cts;

            // Serialize the UI-side handoff with DisposeAsync. Once disposal claims this lock,
            // no callback can enqueue more render-thread work behind its teardown barrier.
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    lock (_renderRequestLock)
                    {
                        if (!_isDisposing && !_isPausing && !token.IsCancellationRequested)
                        {
                            RenderOnRenderThread(token);
                        }
                    }
                },
                Avalonia.Threading.DispatcherPriority.Background,
                token);
        }

        if (previousCts is not null)
        {
            try
            {
                previousCts.Cancel();
            }
            finally
            {
                previousCts.Dispose();
            }
        }
    }

    private void RenderOnRenderThread(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        // Read the onion-skin settings here on the UI thread; reading them on the render thread
        // would race their UI-thread writes.
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
        bool onionEnabled = editorConfig.IsOnionSkinEnabled.Value;
        int onionPrevCount = editorConfig.OnionSkinPrevCount.Value;
        int onionNextCount = editorConfig.OnionSkinNextCount.Value;
        float onionPrevOpacity = editorConfig.OnionSkinPrevOpacity.Value;
        float onionNextOpacity = editorConfig.OnionSkinNextOpacity.Value;

        RenderThread.Dispatcher.Dispatch(
            () => RenderPreviewFrame(
                token, onionEnabled, onionPrevCount, onionNextCount, onionPrevOpacity, onionNextOpacity),
            ct: token);
    }

    // Runs on the render thread, with the onion-skin settings RenderOnRenderThread read on the UI thread.
    private void RenderPreviewFrame(
        CancellationToken token,
        bool onionEnabled,
        int onionPrevCount,
        int onionNextCount,
        float onionPrevOpacity,
        float onionNextOpacity)
    {
        int frame = 0;
        bool useOnionSkin = false;
        int onionSampleCount = 0;
        try
        {
            SceneRenderer renderer = EditViewModel.Renderer.Value;
            FrameCacheManager cacheManager = EditViewModel.FrameCacheManager.Value;
            // Mid-swap the properties can briefly expose a disposed instance (the pair is
            // replaced as two swaps, renderer first). Bail out — the cache swap queues a fresh
            // render once both halves are in place.
            if (renderer is not { IsDisposed: false, IsGraphicsRendering: false }
                || cacheManager.IsDisposed)
                return;
            if (Scene is null)
                return;

            Rational rate = GetFrameRate();
            MediaTime time = _editorClock.CurrentTime.Value;
            frame = (int)time.ToFrame(rate, MidpointRounding.AwayFromZero);
            time = MediaTime.FromFrames(frame, rate);
            // A side contributes nothing when its opacity is 0, so fold opacity into the
            // effective count. When both sides are invisible useOnionSkin stays false and
            // the empty-samples fallback below routes through the cheaper cache-aware path.
            int effectivePrevCount = onionPrevOpacity > 0f ? onionPrevCount : 0;
            int effectiveNextCount = onionNextOpacity > 0f ? onionNextCount : 0;
            useOnionSkin = onionEnabled
                && !IsPlaying.Value
                && (effectivePrevCount > 0 || effectiveNextCount > 0);

            IReadOnlyList<OnionSkinSample> onionSamples = [];
            if (useOnionSkin)
            {
                onionSamples = OnionSkinHelper.EnumerateOnionSkinTimes(
                    frame, Scene.Start.Value, Scene.Duration.Value, rate,
                    effectivePrevCount, effectiveNextCount,
                    onionPrevOpacity, onionNextOpacity);
                onionSampleCount = onionSamples.Count;
                if (onionSamples.Count == 0)
                {
                    // Range clamp emptied everything — fall back to the cache-aware path
                    // instead of taking the heavier (cache-less) onion branch for nothing.
                    _logger.LogDebug(
                        "Onion skin enabled but no samples in range at frame {Frame}; using normal preview.",
                        frame);
                    useOnionSkin = false;
                }
            }

            using Ref<Bitmap> bitmapRef = GetPreviewFrame(renderer, cacheManager, time, frame, useCache: !useOnionSkin);
            Bitmap bitmap = bitmapRef.Value;
            using Ref<Bitmap>? cleanFrame = CopyCleanFrame(renderer, bitmap);
            using (var canvas = new SKCanvas(bitmap.SKBitmap))
            {
                if (useOnionSkin)
                    DrawOnionSkin(renderer, canvas, time, frame, onionSamples, token);
                DrawBoundaries(renderer, canvas, new(bitmap.Width, bitmap.Height));
            }

            if (token.IsCancellationRequested)
                return;

            UpdateImage(bitmapRef, cleanFrame, time);

            // Dispose と並走した場合に AfterRendered が破棄されている可能性があるためトークンを確認する
            if (!token.IsCancellationRequested)
            {
                AfterRendered.OnNext(Unit.Default);
                ClearPreviewRenderError(token);
            }
        }
        catch (ObjectDisposedException) when (token.IsCancellationRequested)
        {
            // Dispose 中のレースで AfterRendered などが破棄された場合のみ無視する。
            // 通常再生中の予期しない ObjectDisposedException は下の catch で表面化させる。
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "An exception occurred while drawing the frame. onionSkin={UseOnionSkin}, sampleCount={Count}, frame={Frame}.",
                useOnionSkin, onionSampleCount, frame);
            ReportPreviewRenderError(token);
        }
    }

    private static Ref<Bitmap> GetPreviewFrame(
        SceneRenderer renderer, FrameCacheManager cacheManager, MediaTime time, int frame, bool useCache)
    {
        var compositionFrame = renderer.Compositor.EvaluateGraphics(time);
        if (useCache && cacheManager.TryGet(frame, out var cache))
        {
            using (cache)
            {
                renderer.UpdateFrame(compositionFrame);
                // The caller will paint editor overlays into this image.
                return Ref<Bitmap>.Create(cache.Value.Clone());
            }
        }

        renderer.Render(compositionFrame);
        using var bitmap = Ref<Bitmap>.Create(renderer.Snapshot());
        // Onion skin bypasses the frame cache, which must contain only the current frame.
        if (useCache && cacheManager.IsEnabled)
        {
            cacheManager.Add(frame, bitmap);
            cacheManager.UpdateBlocks();
        }
        return bitmap.Clone();
    }

    private void DrawOnionSkin(
        SceneRenderer renderer,
        SKCanvas canvas,
        MediaTime time,
        int frame,
        IReadOnlyList<OnionSkinSample> onionSamples,
        CancellationToken token)
    {
        Bitmap? onionScratch = null;

        try
        {
            foreach (var sample in onionSamples)
            {
                // Superseded by a newer render: stop sampling, but still restore the playhead.
                if (token.IsCancellationRequested)
                    break;

                var compFrame = renderer.Compositor.EvaluateGraphics(sample.Time);
                renderer.Render(compFrame);

                // Reuse one scratch bitmap across all onion samples (up to 20).
                onionScratch ??= renderer.CreateSnapshotBitmap();
                renderer.SnapshotInto(onionScratch);

                using var paint = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, (byte)Math.Round(sample.Alpha * 255)),
                    BlendMode = SKBlendMode.SrcOver,
                };

                // This CPU canvas blends synchronously, so the next sample can reuse the scratch.
                canvas.DrawBitmap(onionScratch.SKBitmap, 0, 0, SKSamplingOptions.Default, paint);
            }

            // Selection bounds and subsequent queries must use the current frame's geometry.
            renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(time));
        }
        catch
        {
            // Best-effort: restore the playhead even on failure so later
            // HitTest / GetBoundary use the current frame, not a leftover onion
            // sample. Guard the restore so it can't mask the original exception.
            try
            {
                renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(time));
            }
            catch (Exception restoreEx)
            {
                _logger.LogError(restoreEx,
                    "Failed to restore playhead after onion-skin render failure at frame {Frame}.",
                    frame);
            }

            throw;
        }
        finally
        {
            onionScratch?.Dispose();
        }
    }

    private void UpdateCurrentFrame(MediaTime timeSpan)
    {
        QueueRender();

        if (Scene == null) return;

        if (_editorClock.CurrentTime.Value != timeSpan)
        {
            _editorClock.Seek(timeSpan);
        }
    }
}
