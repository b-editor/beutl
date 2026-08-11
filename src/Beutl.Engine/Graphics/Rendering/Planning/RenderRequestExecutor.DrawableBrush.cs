using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class CompatibilityExecutionState
    {
        private static readonly AsyncLocal<List<Guid>?> s_activeDrawableBrushes = new();

        internal DrawableBrushMaterializer DrawableBrushMaterializer => _drawableBrushMaterializer;

        private RenderExecutionSessionToken CreateExecutionSessionToken()
            => new(_drawableBrushMaterializer);

        private ImmediateCanvas CreateExecutorCanvas(
            RenderTarget target,
            float density,
            float maxWorkingScale,
            Size logicalSize,
            RenderIntent intent,
            PixelPoint deviceOrigin = default)
        {
            ImmediateCanvas canvas = ImmediateCanvas.CreateExecutorManaged(
                target,
                density,
                maxWorkingScale,
                logicalSize,
                intent,
                deviceOrigin);
            canvas.DrawableBrushMaterializer = _drawableBrushMaterializer;
            return canvas;
        }

        private SKImage? MaterializeDrawableBrush(
            DrawableBrush.Resource brush,
            Rect bounds,
            float scale)
        {
            ArgumentNullException.ThrowIfNull(brush);
            RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(bounds));
            if (!float.IsFinite(scale) || scale <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scale), scale, "Drawable-brush density must be positive and finite.");
            }

            if (bounds.Width == 0 || bounds.Height == 0 || brush.Drawable is not { } drawable)
                return null;

            using DrawableBrushCycleScope cycle = EnterDrawableBrush(drawable);
            Rect domain = new(default, bounds.Size);
            PixelRect deviceBounds = PixelRect.FromRect(domain, scale);
            if (deviceBounds.Width == 0 || deviceBounds.Height == 0)
                return null;

            DrawableRenderNode? root = null;
            RenderRequest? request = null;
            CompiledRenderRequest? compiled = null;
            RenderTargetLease? lease = null;
            ImmediateCanvas? canvas = null;
            SKImage? image = null;
            ExceptionDispatchInfo? failure = null;
            try
            {
                root = new DrawableRenderNode(drawable);
                using (var graphics = new GraphicsContext2D(root, domain.Size, scale))
                {
                    drawable.GetOriginal().Render(graphics, drawable);
                }

                request = new RenderRequest(
                    new RenderRequestOptions(
                        _options.Intent,
                        _options.Purpose,
                        targetDomain: domain,
                        requestedRegion: domain,
                        outputScale: scale,
                        maxWorkingScale: _options.MaxWorkingScale,
                        cachePolicy: RenderCacheOptions.Disabled,
                        fusionMode: _options.FusionMode));
                var recorder = new RenderRequestRecorder(request);
                RecordedRenderGraph graph = recorder.Record(root);
                var cacheContext = new RenderCacheResolutionContext(
                    RenderCacheFormatIdentity.LinearPremultipliedRgba16Float,
                    _targets.CacheDeviceContextIdentity,
                    allowPersistentLookup: false,
                    allowCapturePublication: false);
                SkslBackendBudget shaderBudget = SkslBackendBudgetResolver.Resolve(
                    _targets.ExternalTarget?.Value.Context?.Backend);
                compiled = new RenderRequestCompiler(
                        structuralPlanCache: null,
                        renderCacheContext: cacheContext,
                        renderCacheLookup: null)
                    .Compile(request, graph, shaderBudget);
                request = null;

                lease = _targets.TryAcquire(deviceBounds.Size);
                if (lease is not null)
                {
                    Rect rasterBounds = deviceBounds.ToRect(scale);
                    canvas = CreateExecutorCanvas(
                        lease.Target,
                        scale,
                        _options.MaxWorkingScale,
                        rasterBounds.Size,
                        _options.Intent,
                        deviceBounds.Position);
                    canvas.Clear();

                    var executor = new RenderRequestExecutor(_targets, _programCache);
                    executor.Execute(compiled, canvas);
                    canvas.CloseWithoutFlush();
                    canvas = null;

                    lease.Target.PrepareForSampling();
                    image = CreateIndependentImage(lease.Target.Value);
                }
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                CloseAndCapture(canvas, ref failure);
                DisposeAndCapture(lease, ref failure);
                DisposeAndCapture(compiled, ref failure);
                DisposeAndCapture(request, ref failure);
                DisposeAndCapture(root, ref failure);
            }

            if (failure is not null)
            {
                DisposeAndCapture(image, ref failure);
                failure!.Throw();
            }

            return image;
        }

        private static DrawableBrushCycleScope EnterDrawableBrush(Drawable.Resource drawable)
        {
            Guid identity = EngineResourceIdentity.Of(drawable);
            List<Guid> active = s_activeDrawableBrushes.Value ??= [];
            int cycleStart = active.IndexOf(identity);
            if (cycleStart >= 0)
            {
                IEnumerable<Guid> cycle = active.Skip(cycleStart).Append(identity);
                throw new InvalidOperationException(
                    $"A drawable-brush materialization cycle was detected: {string.Join(" -> ", cycle)}.");
            }

            active.Add(identity);
            return new DrawableBrushCycleScope(active, identity);
        }

        private static SKImage CreateIndependentImage(SKSurface surface)
        {
            SKImage snapshot = surface.Snapshot();
            try
            {
                SKImage raster = snapshot.ToRasterImage()
                    ?? throw new InvalidOperationException(
                        "The drawable-brush surface could not be copied to an independent image.");
                if (ReferenceEquals(snapshot, raster))
                    return snapshot;

                snapshot.Dispose();
                return raster;
            }
            catch
            {
                snapshot.Dispose();
                throw;
            }
        }

        private static void CloseAndCapture(
            ImmediateCanvas? canvas,
            ref ExceptionDispatchInfo? failure)
        {
            if (canvas is null)
                return;

            try
            {
                canvas.CloseWithoutFlush();
            }
            catch (Exception ex)
            {
                CaptureCleanupFailure(ex, ref failure);
            }
        }

        private static void DisposeAndCapture(
            IDisposable? resource,
            ref ExceptionDispatchInfo? failure)
        {
            if (resource is null)
                return;

            try
            {
                resource.Dispose();
            }
            catch (Exception ex)
            {
                CaptureCleanupFailure(ex, ref failure);
            }
        }

        private static void CaptureCleanupFailure(
            Exception cleanupFailure,
            ref ExceptionDispatchInfo? failure)
        {
            if (failure is null)
            {
                failure = ExceptionDispatchInfo.Capture(cleanupFailure);
                return;
            }

            const string key = "DrawableBrushMaterializationCleanupFailure";
            Exception primary = failure.SourceException;
            primary.Data[key] = primary.Data[key] is Exception previous
                ? new AggregateException(previous, cleanupFailure)
                : cleanupFailure;
        }

        private readonly struct DrawableBrushCycleScope(
            List<Guid> active,
            Guid identity) : IDisposable
        {
            public void Dispose()
            {
                int index = active.Count - 1;
                if (index < 0 || active[index] != identity)
                    throw new InvalidOperationException("The drawable-brush materialization stack is corrupted.");

                active.RemoveAt(index);
                if (active.Count == 0 && ReferenceEquals(s_activeDrawableBrushes.Value, active))
                    s_activeDrawableBrushes.Value = null;
            }
        }
    }
}
