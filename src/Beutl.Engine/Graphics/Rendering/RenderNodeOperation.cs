using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNodeOperation : IDisposable
{
    public bool IsDisposed { get; private set; }

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
        if (!IsDisposed)
        {
            OnDispose(true);
            IsDisposed = true;
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
        foreach (var op in ops)
        {
            try
            {
                op.Dispose();
            }
            catch
            {
                // Best-effort: a faulting Dispose must not stop the remaining ops from being released.
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
            try
            {
                child.Dispose();
            }
            finally
            {
                onDispose?.Invoke();
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
