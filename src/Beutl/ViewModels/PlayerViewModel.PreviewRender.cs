using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Platforms.XAudio2;
using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.ViewModels;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Models;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D.Gizmo;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Silk.NET.OpenAL;
using SkiaSharp;
using Vortice.Multimedia;
using AudioContext = Beutl.Audio.Platforms.OpenAL.AudioContext;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    private void UpdateImage(Ref<Bitmap> source)
    {
        var oldBitmap = PreviewImage.Value;
        PreviewImage.Value = source;
        oldBitmap?.Dispose();

        PreviewInvalidated?.Invoke(this, EventArgs.Empty);
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

    internal void SetPreviewRenderError(string? message)
    {
        UpdatePreviewRenderError(message, static () => true);
    }

    internal bool ApplyPlaybackRenderFailure(
        BufferedPlayer.RenderFailure failure,
        int rate,
        Func<bool> isCurrent)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(isCurrent);
        if (Scene is null || !isCurrent())
            return false;

        _editorClock.CurrentTime.Value = failure.Frame.ToTimeSpan(rate);
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

    private void DrawBoundaries(Renderer renderer, SKCanvas canvas, Size canvasSize, bool recalculate = false)
    {
        int? selected = _editorSelection.SelectedLayerNumber.Value;
        if (selected.HasValue)
        {
            var frameSize = new Size(renderer.FrameSize.Width, renderer.FrameSize.Height);
            var frameScale = canvasSize.Width / frameSize.Width;
            float strokeScale = Stretch.Uniform.CalculateScaling(MaxFrameSize, frameSize).X;
            if (strokeScale < 1)
                strokeScale = 1;

            // フレームキャッシュを使う場合はBoundsを再計算する必要がある
            Rect[] boundary = recalculate
                ? renderer.RecalculateBoundaries(selected.Value)
                : renderer.GetBoundaries(selected.Value);
            if (boundary.Length > 0)
            {
                using var paint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = strokeScale
                };
                bool exactBounds = GlobalConfiguration.Instance.ViewConfig.ShowExactBoundaries;

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

        // Snapshot the onion-skin config here on the UI thread (RenderOnRenderThread is
        // invoked via Dispatcher.UIThread). Reading these CoreProperty getters inside the
        // render-thread dispatch below would race the UI-thread write-back subscriptions
        // against CoreObject's non-synchronized value dictionary.
        EditorConfig editorConfig = GlobalConfiguration.Instance.EditorConfig;
        bool onionEnabled = editorConfig.IsOnionSkinEnabled;
        int onionPrevCount = editorConfig.OnionSkinPrevCount;
        int onionNextCount = editorConfig.OnionSkinNextCount;
        float onionPrevOpacity = editorConfig.OnionSkinPrevOpacity;
        float onionNextOpacity = editorConfig.OnionSkinNextOpacity;

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

            int rate = GetFrameRate();
            TimeSpan time = _editorClock.CurrentTime.Value;
            frame = (int)Math.Round(time.ToFrameNumber(rate), MidpointRounding.AwayFromZero);
            time = frame.ToTimeSpan(rate);
            Ref<Bitmap>? bitmapRef;

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
                    frame, Scene.Start, Scene.Duration, rate,
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

            if (useOnionSkin)
            {
                bitmapRef = RenderOnionSkinFrame(renderer, time, frame, onionSamples, token);
            }
            else if (cacheManager.TryGet(frame, out var cache))
            {
                bitmapRef = RenderCachedFrame(renderer, time, cache);
            }
            else
            {
                bitmapRef = RenderAndCacheFrame(renderer, cacheManager, time, frame);
            }

            if (token.IsCancellationRequested)
            {
                bitmapRef.Dispose();
                return;
            }

            UpdateImage(bitmapRef);

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

    private Ref<Bitmap> RenderOnionSkinFrame(
        SceneRenderer renderer,
        TimeSpan time,
        int frame,
        IReadOnlyList<OnionSkinSample> onionSamples,
        CancellationToken token)
    {
        // Render the current frame; its Snapshot bitmap doubles as the composition
        // canvas. Onion-composited frames are intentionally NOT pushed into
        // cacheManager: the cache is keyed on frame number alone, and an
        // onion-disabled re-render would otherwise return the composite.
        var currentCompositionFrame = renderer.Compositor.EvaluateGraphics(time);
        renderer.Render(currentCompositionFrame);
        Bitmap? currentBitmap = null;
        Bitmap? onionScratch = null;

        try
        {
            currentBitmap = renderer.Snapshot();
            using (var canvas = new SKCanvas(currentBitmap.SKBitmap))
            {
                foreach (var sample in onionSamples)
                {
                    // A newer QueueRender has superseded this pass (e.g. the user
                    // kept scrubbing); stop re-rendering samples instead of grinding
                    // through all of them. Break (not return) so the playhead restore
                    // and boundary draw below still run, leaving the renderer on the
                    // current frame; the partial composite is replaced by the queued render.
                    if (token.IsCancellationRequested)
                        break;

                    // Belt-and-suspenders for the mixed case (one side opaque, the
                    // other at zero opacity): skip the render/snapshot/blend for any
                    // sample that would composite nothing.
                    if (sample.Alpha <= 0f)
                        continue;

                    var compFrame = renderer.Compositor.EvaluateGraphics(sample.Time);
                    renderer.Render(compFrame);

                    // Reuse one scratch bitmap across all onion samples (up to 20),
                    // turning per-sample LOH allocations into a single one.
                    // CreateSnapshotBitmap gives it the format SnapshotInto requires.
                    onionScratch ??= renderer.CreateSnapshotBitmap();
                    renderer.SnapshotInto(onionScratch);

                    using var paint = new SKPaint
                    {
                        Color = new SKColor(255, 255, 255, (byte)Math.Round(sample.Alpha * 255)),
                        BlendMode = SKBlendMode.SrcOver,
                    };

                    // canvas is a CPU raster SKCanvas, so DrawBitmap blends the scratch
                    // pixels synchronously — safe to overwrite onionScratch next sample.
                    canvas.DrawBitmap(onionScratch.SKBitmap, 0, 0, SKSamplingOptions.Default, paint);
                }

                // Restore renderer entries to the playhead BEFORE drawing
                // boundaries so the selection box uses the current frame's
                // geometry, not the last onion sample's.
                renderer.UpdateFrame(renderer.Compositor.EvaluateGraphics(time));

                DrawBoundaries(renderer, canvas, new(currentBitmap.Width, currentBitmap.Height), true);
            }

            // Ownership moves to the returned Ref here; this is the last statement in the
            // try, so the catch below only ever disposes currentBitmap on a path
            // before this point (no double-dispose).
            return Ref<Bitmap>.Create(currentBitmap);
        }
        catch
        {
            currentBitmap?.Dispose();
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

    private Ref<Bitmap> RenderCachedFrame(SceneRenderer renderer, TimeSpan time, Ref<Bitmap> cache)
    {
        using (cache)
        {
            var compositionFrame = renderer.Compositor.EvaluateGraphics(time);

            renderer.UpdateFrame(compositionFrame);
            var bitmap = cache.Value.Clone();
            Ref<Bitmap> bitmapRef = Ref<Bitmap>.Create(bitmap);

            using (var canvas = new SKCanvas(bitmap.SKBitmap))
            {
                DrawBoundaries(renderer, canvas, new(bitmap.Width, bitmap.Height), true);
            }

            return bitmapRef;
        }
    }

    private Ref<Bitmap> RenderAndCacheFrame(
        SceneRenderer renderer, FrameCacheManager cacheManager, TimeSpan time, int frame)
    {
        var compositionFrame = renderer.Compositor.EvaluateGraphics(time);
        renderer.Render(compositionFrame);
        var bitmap = renderer.Snapshot();
        Ref<Bitmap> bitmapRef = Ref<Bitmap>.Create(bitmap);

        if (cacheManager.IsEnabled)
        {
            cacheManager.Add(frame, bitmapRef);
            cacheManager.UpdateBlocks();
        }

        using (var canvas = new SKCanvas(bitmap.SKBitmap))
        {
            DrawBoundaries(renderer, canvas, new(bitmap.Width, bitmap.Height), true);
        }

        return bitmapRef;
    }

    private void UpdateCurrentFrame(TimeSpan timeSpan)
    {
        QueueRender();

        if (Scene == null) return;

        if (_editorClock.CurrentTime.Value != timeSpan)
        {
            _editorClock.CurrentTime.Value = timeSpan;
        }
    }
}
