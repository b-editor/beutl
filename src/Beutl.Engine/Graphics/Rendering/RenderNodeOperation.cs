using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

public abstract class RenderNodeOperation : IDisposable
{
    public bool IsDisposed { get; private set; }

    // Invalidになることはない
    public abstract Rect Bounds { get; }

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
        });
    }

    public static RenderNodeOperation CreateLambda(
        Rect bounds, Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest = null,
        Action? onDispose = null)
    {
        return new LambdaRenderNodeOperation(bounds, render, hitTest, onDispose);
    }

    public static RenderNodeOperation CreateFromRenderTarget(Rect bounds, Point position, RenderTarget renderTarget)
    {
        return CreateLambda(bounds, canvas => canvas.DrawSurface(renderTarget.Value, position), bounds.Contains, renderTarget.Dispose);
    }

    public static RenderNodeOperation CreateFromSurface(Rect bounds, Point position, SKSurface surface)
    {
        return CreateLambda(bounds, canvas => canvas.DrawSurface(surface, position), bounds.Contains, surface.Dispose);
    }

    public static RenderNodeOperation CreateFromSurface(Rect bounds, Point position, Ref<SKSurface> surface)
    {
        return CreateLambda(bounds, canvas => canvas.DrawSurface(surface.Value, position), bounds.Contains, surface.Dispose);
    }

    private class LambdaRenderNodeOperation(
        Rect bounds,
        Action<ImmediateCanvas> render,
        Func<Point, bool>? hitTest,
        Action? onDispose)
        : RenderNodeOperation
    {
        public override Rect Bounds => bounds;

        public override void Render(ImmediateCanvas canvas) => render(canvas);

        public override bool HitTest(Point point) => hitTest?.Invoke(point) ?? false;

        protected override void OnDispose(bool disposing) => onDispose?.Invoke();
    }
}
