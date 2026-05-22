using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNodeOperation : IDisposable
{
    public bool IsDisposed { get; private set; }

    // Invalidになることはない
    public abstract Rect Bounds { get; }

    /// <summary>
    /// The scale ratio between this operation's <see cref="Bounds"/> (authoring space) and the
    /// raster produced by <see cref="Render(ImmediateCanvas)"/>.
    /// </summary>
    /// <remarks>
    /// Default is <see cref="RenderScale.Identity"/> — i.e. "raster matches bounds 1:1, no proxy".
    /// Source-producing nodes that apply per-clip proxy override this to declare the upscale ratio
    /// (e.g. <c>(4, 4)</c> when the raster is 1/4 the linear size of the bounds).
    /// Transformer nodes propagate the upstream value unchanged.
    /// </remarks>
    public virtual RenderScale CorrectionScale => RenderScale.Identity;

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
        return CreateLambda(child.Bounds, render, hitTest: hitTest ?? child.HitTest, onDispose: () =>
        {
            child.Dispose();
            onDispose?.Invoke();
        }, correctionScale: child.CorrectionScale);
    }

    public static RenderNodeOperation CreateLambda(
        Rect bounds, Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest = null,
        Action? onDispose = null,
        RenderScale correctionScale = default)
    {
        return new LambdaRenderNodeOperation(bounds, render, hitTest, onDispose, NormalizeScale(correctionScale));
    }

    public static RenderNodeOperation CreateFromRenderTarget(
        Rect bounds, Point position, RenderTarget renderTarget,
        RenderScale correctionScale = default)
    {
        return CreateLambda(
            bounds,
            canvas => canvas.DrawRenderTarget(renderTarget, position),
            bounds.Contains,
            renderTarget.Dispose,
            correctionScale);
    }

    public static RenderNodeOperation CreateFromSurface(
        Rect bounds, Point position, SKSurface surface,
        RenderScale correctionScale = default)
    {
        return CreateLambda(
            bounds,
            canvas => canvas.DrawSurface(surface, position),
            bounds.Contains,
            surface.Dispose,
            correctionScale);
    }

    public static RenderNodeOperation CreateFromSurface(
        Rect bounds, Point position, Ref<SKSurface> surface,
        RenderScale correctionScale = default)
    {
        return CreateLambda(
            bounds,
            canvas => canvas.DrawSurface(surface.Value, position),
            bounds.Contains,
            surface.Dispose,
            correctionScale);
    }

    // `default(RenderScale)` is (0, 0), which is invalid per the type's contract.
    // Existing callers that pass no argument expect Identity; substitute here so the public
    // surface stays back-compatible without forcing every site to specify `RenderScale.Identity`.
    private static RenderScale NormalizeScale(RenderScale scale) =>
        scale.ScaleX == 0f && scale.ScaleY == 0f ? RenderScale.Identity : scale;

    private sealed class LambdaRenderNodeOperation(
        Rect bounds,
        Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest,
        Action? onDispose,
        RenderScale correctionScale)
        : RenderNodeOperation
    {
        public override Rect Bounds => bounds;

        public override RenderScale CorrectionScale => correctionScale;

        public override void Render(ImmediateCanvas canvas) => render(canvas);

        public override bool HitTest(Point point) => hitTest?.Invoke(point) ?? false;

        protected override void OnDispose(bool disposing) => onDispose?.Invoke();
    }
}
