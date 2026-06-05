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
        return CreateLambda(bounds, canvas => canvas.DrawRenderTarget(renderTarget, position), bounds.Contains, renderTarget.Dispose, effectiveScale);
    }

    public static RenderNodeOperation CreateFromSurface(
        Rect bounds, Point position, SKSurface surface, EffectiveScale effectiveScale = default)
    {
        return CreateLambda(bounds, canvas => canvas.DrawSurface(surface, position), bounds.Contains, surface.Dispose, effectiveScale);
    }

    public static RenderNodeOperation CreateFromSurface(
        Rect bounds, Point position, Ref<SKSurface> surface, EffectiveScale effectiveScale = default)
    {
        return CreateLambda(bounds, canvas => canvas.DrawSurface(surface.Value, position), bounds.Contains, surface.Dispose, effectiveScale);
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
