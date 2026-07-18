using System.Runtime.ExceptionServices;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNodeOperation : IDisposable
{
    private const int ActiveDisposeState = 0;
    private const int DisposingState = 1;
    private const int DisposedState = 2;

    private int _disposeState;

    public bool IsDisposed => Volatile.Read(ref _disposeState) == DisposedState;

    // Invalidになることはない
    public abstract Rect Bounds { get; }

    /// <summary>
    /// Supply density: <see cref="EffectiveScale.Unbounded"/> for vector ops, concrete <see cref="EffectiveScale.At"/> for bitmaps.
    /// </summary>
    public virtual EffectiveScale EffectiveScale => EffectiveScale.Unbounded;

    public abstract void Render(ImmediateCanvas canvas);

    public abstract bool HitTest(Point point);

    public void Dispose()
    {
        if (Interlocked.CompareExchange(
                ref _disposeState,
                DisposingState,
                ActiveDisposeState) != ActiveDisposeState)
        {
            return;
        }

        try
        {
            OnDispose(true);
        }
        finally
        {
            Volatile.Write(ref _disposeState, DisposedState);
            GC.SuppressFinalize(this);
        }
    }

    protected virtual void OnDispose(bool disposing)
    {
    }

    /// <summary>
    /// Disposes every operation in <paramref name="ops"/>, swallowing an individual <see cref="Dispose"/>
    /// fault so one throwing op cannot abort the sweep. Used to release the ops a loop never reached after a throw.
    /// </summary>
    internal static void DisposeAll(ReadOnlySpan<RenderNodeOperation> ops)
    {
        Exception? ignored = null;
        DisposeAll(ops, ref ignored);
    }

    /// <summary>
    /// Disposes every operation and records the first cleanup failure without stopping the sweep.
    /// A caller with an in-flight primary failure can ignore the captured cleanup failure; a successful
    /// operation can rethrow it after all resources have been released.
    /// </summary>
    internal static void DisposeAll(ReadOnlySpan<RenderNodeOperation> ops, ref Exception? failure)
    {
        foreach (var op in ops)
        {
            if (op is null)
                continue;

            try
            {
                op.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }
        }
    }

    public static RenderNodeOperation CreateDecorator(
        RenderNodeOperation child, Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest = null,
        Action? onDispose = null)
    {
        return CreateLambda(child.Bounds, render, hitTest: hitTest ?? child.HitTest, onDispose: () =>
        {
            Exception? failure = null;
            try
            {
                child.Dispose();
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                onDispose?.Invoke();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }, effectiveScale: child.EffectiveScale);
    }

    public static RenderNodeOperation CreateLambda(
        Rect bounds, Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest = null,
        Action? onDispose = null,
        EffectiveScale effectiveScale = default)
    {
        return new LambdaRenderNodeOperation(bounds, render, hitTest, onDispose, effectiveScale);
    }

    public static RenderNodeOperation CreateFromRenderTarget(
        Rect bounds, Point position, RenderTarget renderTarget, EffectiveScale effectiveScale = default)
    {
        // Dest size comes from the buffer footprint (pixels / density), not from bounds.
        Action<ImmediateCanvas> render = effectiveScale.IsUnbounded || effectiveScale.Value == 1f
            ? canvas =>
            {
                if (canvas.Density == 1f)
                    canvas.DrawRenderTarget(renderTarget, position);
                else
                    canvas.DrawRenderTargetScaled(renderTarget, new Rect(
                        position.X, position.Y, renderTarget.Width, renderTarget.Height));
            }
        : canvas => canvas.DrawRenderTargetScaled(renderTarget, new Rect(
            bounds.X, bounds.Y,
            renderTarget.Width / effectiveScale.Value, renderTarget.Height / effectiveScale.Value));
        return CreateLambda(bounds, render, bounds.Contains, renderTarget.Dispose, effectiveScale);
    }

    public static RenderNodeOperation CreateFromSurface(
        Rect bounds, Point position, SKSurface surface, EffectiveScale effectiveScale = default)
    {
        Action<ImmediateCanvas> render = effectiveScale.IsUnbounded || effectiveScale.Value == 1f
            ? canvas =>
            {
                if (canvas.Density == 1f)
                    canvas.DrawSurface(surface, position);
                else
                    canvas.DrawSurfaceScaled(surface, position, 1f);
            }
        : canvas => canvas.DrawSurfaceScaled(surface, bounds.Position, effectiveScale.Value);
        return CreateLambda(bounds, render, bounds.Contains, surface.Dispose, effectiveScale);
    }

    public static RenderNodeOperation CreateFromSurface(
        Rect bounds, Point position, Ref<SKSurface> surface, EffectiveScale effectiveScale = default)
    {
        Action<ImmediateCanvas> render = effectiveScale.IsUnbounded || effectiveScale.Value == 1f
            ? canvas =>
            {
                if (canvas.Density == 1f)
                    canvas.DrawSurface(surface.Value, position);
                else
                    canvas.DrawSurfaceScaled(surface.Value, position, 1f);
            }
        : canvas => canvas.DrawSurfaceScaled(surface.Value, bounds.Position, effectiveScale.Value);
        return CreateLambda(bounds, render, bounds.Contains, surface.Dispose, effectiveScale);
    }

    private class LambdaRenderNodeOperation(
        Rect bounds,
        Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest,
        Action? onDispose,
        EffectiveScale effectiveScale)
        : RenderNodeOperation
    {
        public override Rect Bounds => bounds;

        public override EffectiveScale EffectiveScale => effectiveScale;

        public override void Render(ImmediateCanvas canvas) => render(canvas);

        public override bool HitTest(Point point) => hitTest?.Invoke(point) ?? false;

        protected override void OnDispose(bool disposing) => onDispose?.Invoke();
    }
}
