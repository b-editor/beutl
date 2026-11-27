using Beutl.Composition;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Models;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using SkiaSharp;

namespace Beutl.ViewModels;

public partial class PlayerViewModel
{
    // Guarded by _renderRequestLock. Scene edits can request one follow-up while a preview is in flight.
    private bool _previewRenderScheduled;
    private bool _previewEditPending;

    // Both displayed images retain their own reference; the caller still owns the supplied frames.
    private void UpdateImage(Ref<Bitmap> source, Ref<Bitmap>? cleanSource, MediaTime time, long playbackId = 0,
        IEditingStageProvider? editingStage = null, GraphEditingFrame? graphEditingFrame = null,
        EffectComparisonFrame? comparison = null)
    {
        lock (_cleanPreviewLock)
        {
            _cleanPreviewTime = time;
            _cleanPreviewPlaybackId = playbackId;
            ReplaceImage(CleanPreviewImage, _cleanPreviewEnabled ? cleanSource : null);
        }

        _graphEditingFrame = graphEditingFrame;
        if (comparison is not null)
            UpdateEffectComparisonImage(source, comparison);
        ReplaceImage(PreviewImage, source);
        ShownEditingStage.Value = editingStage;

        PreviewInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private static void ReplaceImage(ReactivePropertySlim<Ref<Bitmap>?> target, Ref<Bitmap>? source)
    {
        var previous = target.Value;
        target.Value = source?.Clone();
        previous?.Dispose();
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
        UpdatePreviewRenderStatus(MessageStrings.FrameDrawingException, warning: null, isCurrent);
        IsPlaying.Value = false;
        return true;
    }

    private void UpdatePreviewRenderStatus(string? error, string? warning, Func<bool> isCurrent)
    {
        if (!isCurrent())
            return;

        long version = Interlocked.Increment(ref _previewRenderStatusIssuedVersion);

        void Update()
        {
            // A queued update may run after this view model was disposed or its render/playback
            // request was superseded. Keep the status scoped to the preview that still owns it.
            // Invalid newer updates do not advance the applied version, so they cannot suppress
            // an older update that is still valid for the current request.
            if (Scene is not null
                && isCurrent()
                && version > _previewRenderStatusAppliedVersion)
            {
                _previewRenderStatusAppliedVersion = version;
                _previewRenderError.Value = error;
                _previewRenderWarning.Value = warning;
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

    // Re-render the current frame into the viewport. Used when something outside the normal edit /
    // playback path (e.g. a PreviewSourceMode switch) invalidates the shown frame while paused.
    public void QueuePreviewRender() => QueueRender();

    private void QueueRender(bool coalesceSceneEdits = false)
    {
        CancellationTokenSource? previousCts;
        lock (_renderRequestLock)
        {
            if (_isDisposing || _isPausing)
                return;

            // Fast drags can outpace rendering. Keep the current render and fold the newer scene edits
            // into one follow-up, so the preview can keep presenting frames while the pointer moves.
            if (coalesceSceneEdits && _cts is not null && _previewRenderScheduled)
            {
                _previewEditPending = true;
                return;
            }

            previousCts = _cts;
            _cts = new CancellationTokenSource();
            SchedulePreviewRender(_cts.Token);
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

    // Called with _renderRequestLock held. The UI handoff shares the lock with pause and teardown.
    private void SchedulePreviewRender(CancellationToken token)
    {
        _previewRenderScheduled = true;
        _previewEditPending = false;
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
            () =>
            {
                lock (_renderRequestLock)
                {
                    if (_isDisposing || _isPausing || _cts?.Token != token)
                        return;

                    // This render already includes edits that arrived before the handoff.
                    _previewEditPending = false;
                    try
                    {
                        RenderOnRenderThread(token);
                    }
                    catch
                    {
                        _previewRenderScheduled = false;
                        throw;
                    }
                }
            },
            Avalonia.Threading.DispatcherPriority.Background,
            token);
    }

    private void CompletePreviewRender(CancellationToken token)
    {
        lock (_renderRequestLock)
        {
            if (_cts?.Token != token)
                return;

            _previewRenderScheduled = false;
            if (_previewEditPending && !_isDisposing && !_isPausing)
                SchedulePreviewRender(token);
        }
    }

    private void RenderOnRenderThread(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        // Read the onion-skin settings here on the UI thread; reading them on the render thread would race their
        // UI-thread writes.
        EditorConfig editorConfig = EditViewModel.GetRequiredService<GlobalConfiguration>().EditorConfig;
        bool onionEnabled = editorConfig.IsOnionSkinEnabled.Value;
        int onionPrevCount = editorConfig.OnionSkinPrevCount.Value;
        int onionNextCount = editorConfig.OnionSkinNextCount.Value;
        float onionPrevOpacity = editorConfig.OnionSkinPrevOpacity.Value;
        float onionNextOpacity = editorConfig.OnionSkinNextOpacity.Value;
        EffectComparisonTarget? comparison = ComparisonTarget;
        IEditingStageProvider? stage = IsPlaybackActive || comparison is not null ? null : _editingStageEffect.Value;
        IReadOnlySet<EngineObject>? disabledEffects = GetEditingStageDisabledEffects(stage);
        RenderThread.Dispatcher.Dispatch(
            () => RenderPreviewFrame(
                token, onionEnabled, onionPrevCount, onionNextCount, onionPrevOpacity, onionNextOpacity,
                stage, disabledEffects, comparison),
            ct: token);
    }

    // Runs on the render thread, with the settings RenderOnRenderThread read on the UI thread.
    private void RenderPreviewFrame(
        CancellationToken token,
        bool onionEnabled,
        int onionPrevCount,
        int onionNextCount,
        float onionPrevOpacity,
        float onionNextOpacity,
        IEditingStageProvider? stage,
        IReadOnlySet<EngineObject>? disabledEffects,
        EffectComparisonTarget? comparison)
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

            // Draw the bypass first so the normal side leaves its geometry and effect state in the renderer.
            using EffectComparisonFrame? comparisonFrame = comparison is null ? null : RenderComparisonBefore(renderer, time, comparison);
            using Ref<Bitmap> bitmapRef = GetPreviewFrame(renderer, cacheManager, time, frame, disabledEffects, stage as FilterEffect,
                useCache: !useOnionSkin && disabledEffects is null && comparison is null,
                out GraphEditingFrame? graphEditingFrame, out string? warning);
            Bitmap bitmap = bitmapRef.Value;
            using Ref<Bitmap>? cleanFrame = CopyCleanFrame(renderer, bitmap);
            if (useOnionSkin)
            {
                using var canvas = new SKCanvas(bitmap.SKBitmap);
                using var beforeCanvas = comparisonFrame is null ? null : new SKCanvas(comparisonFrame.Before.Value.SKBitmap);
                DrawOnionSkin(renderer, canvas, beforeCanvas, time, frame, onionSamples, disabledEffects, token);
            }

            if (token.IsCancellationRequested || comparisonFrame is not null && ComparisonRenderWasSuperseded(token))
                return;

            UpdateImage(bitmapRef, cleanFrame, time, editingStage: disabledEffects is null ? null : stage,
                graphEditingFrame: graphEditingFrame, comparison: comparisonFrame);

            // Dispose と並走した場合に AfterRendered が破棄されている可能性があるためトークンを確認する
            if (!token.IsCancellationRequested)
            {
                AfterRendered.OnNext(Unit.Default);
                UpdatePreviewRenderStatus(error: null, warning, () => !token.IsCancellationRequested);
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
            UpdatePreviewRenderStatus(MessageStrings.FrameDrawingException, warning: null,
                () => !token.IsCancellationRequested);
        }
        finally
        {
            CompletePreviewRender(token);
        }
    }

    private static CompositionFrame EvaluatePreviewFrame(
        SceneRenderer renderer, MediaTime time, IReadOnlySet<EngineObject>? disabledEffects)
    {
        SceneCompositor compositor = renderer.Compositor;
        if (disabledEffects is null)
            return compositor.EvaluateGraphics(time);

        IReadOnlySet<EngineObject>? previous = compositor.DisabledObjects;
        try
        {
            compositor.DisabledObjects = disabledEffects;
            return compositor.EvaluateGraphics(time);
        }
        finally
        {
            compositor.DisabledObjects = previous;
        }
    }

    private bool ComparisonRenderWasSuperseded(CancellationToken token)
    {
        // A scene edit arriving between the two draws queues another render. Publish that pair instead.
        lock (_renderRequestLock)
            return _cts?.Token != token || _previewEditPending;
    }

    private static Ref<Bitmap> GetPreviewFrame(
        SceneRenderer renderer, FrameCacheManager cacheManager, MediaTime time, int frame,
        IReadOnlySet<EngineObject>? disabledEffects, FilterEffect? stage, bool useCache,
        out GraphEditingFrame? graphEditingFrame, out string? warning)
    {
        warning = null;
        graphEditingFrame = null;
        using var placement = stage is not null && FindEditingGraph(stage) is { } graph
            ? EffectEditingPlacement.Begin(stage, graph)
            : null;
        useCache &= placement is null;
        CompositionFrame compositionFrame = EvaluatePreviewFrame(renderer, time, disabledEffects);
        if (useCache && cacheManager.TryGet(frame, out var cache, out warning))
        {
            using (cache)
            {
                renderer.UpdateFrame(compositionFrame);
                // The caller will paint editor overlays into this image.
                return Ref<Bitmap>.Create(cache.Value.Clone());
            }
        }

        warning = renderer.RenderWithWarning(compositionFrame);
        if (placement is not null)
            graphEditingFrame = new GraphEditingFrame(stage!, time, placement.GetTransform());
        using var bitmap = Ref<Bitmap>.Create(renderer.Snapshot());
        // Onion skin, comparison and an editing stage bypass the normal frame cache.
        if (useCache && cacheManager.IsEnabled)
        {
            cacheManager.Add(frame, bitmap, warning);
            cacheManager.UpdateBlocks();
        }
        return bitmap.Clone();
    }

    private void DrawOnionSkin(
        SceneRenderer renderer,
        SKCanvas canvas,
        SKCanvas? comparisonCanvas,
        MediaTime time,
        int frame,
        IReadOnlyList<OnionSkinSample> onionSamples,
        IReadOnlySet<EngineObject>? disabledEffects,
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

                var compFrame = EvaluatePreviewFrame(renderer, sample.Time, disabledEffects);
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
                comparisonCanvas?.DrawBitmap(onionScratch.SKBitmap, 0, 0, SKSamplingOptions.Default, paint);
            }

            // Selection bounds and subsequent queries must use the current frame's geometry.
            renderer.UpdateFrame(EvaluatePreviewFrame(renderer, time, disabledEffects));
        }
        catch
        {
            // Best-effort: restore the playhead even on failure so later
            // HitTest / GetBoundary use the current frame, not a leftover onion
            // sample. Guard the restore so it can't mask the original exception.
            try
            {
                renderer.UpdateFrame(EvaluatePreviewFrame(renderer, time, disabledEffects));
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
