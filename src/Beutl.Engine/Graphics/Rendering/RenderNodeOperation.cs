using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNodeOperation : IDisposable
{
    public bool IsDisposed { get; private set; }

    // Invalidになることはない
    public abstract Rect Bounds { get; }

    /// <summary>
    /// The supply density this operation's backing pixels exist at (feature 003). Vector / lossless
    /// operations report <see cref="EffectiveScale.Unbounded"/> (the default) and re-rasterize at any
    /// target scale; bitmap-backed operations report a concrete density via <see cref="EffectiveScale.At"/>.
    /// Flows bottom-up so a container can reconcile mixed-scale inputs.
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

    public static RenderNodeOperation CreateDecorator(
        RenderNodeOperation child, Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest = null,
        Action? onDispose = null)
    {
        // A decorator inherits its child's supply density (Unbounded for vector children).
        return CreateLambda(child.Bounds, render, hitTest: hitTest ?? child.HitTest, onDispose: () =>
        {
            child.Dispose();
            onDispose?.Invoke();
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
        // feature 003: a concrete-density buffer (At(w)) is drawn so the active CTM resamples it once; the
        // scaled dest derives its SIZE from the buffer footprint (RT pixels ÷ density), NOT from `bounds`, so
        // a downstream filter that inflated `bounds` while the buffer still holds the original area cannot
        // stretch it (mirrors EffectTarget.Draw); `bounds.Position` is kept so the buffer lands where it
        // belongs. A density-1 buffer keeps the bare point blit ONLY on a density-1 canvas (byte-identical
        // pre-feature path); on a scaled canvas the point blit would be resampled by the CTM with NEAREST
        // sampling, so route it through the Mitchell blit instead — same geometry, consistent kernel.
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
        // feature 003: scaled branch derives its dest from the surface footprint (pixels ÷ density),
        // anchored at bounds.Position — mirroring CreateFromRenderTarget so an inflated `bounds` cannot
        // stretch it. Density-1 point blit only on a density-1 canvas (see CreateFromRenderTarget).
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
