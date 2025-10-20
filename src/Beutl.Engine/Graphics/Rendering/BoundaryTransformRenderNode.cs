using Beutl.Engine;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

// TODO: 実験的
public sealed class BoundaryTransformRenderNode(
    Transform.Resource transform,
    RelativePoint transformOrigin,
    Size screenSize,
    AlignmentX alignmentX,
    AlignmentY alignmentY) : ContainerRenderNode
{
    public (Transform.Resource Resource, int Version)? Transform { get; private set; } = transform.Capture();

    public RelativePoint TransformOrigin { get; private set; } = transformOrigin;

    public Size ScreenSize { get; private set; } = screenSize;

    public AlignmentX AlignmentX { get; private set; } = alignmentX;

    public AlignmentY AlignmentY { get; private set; } = alignmentY;

    public bool Update(
        Transform.Resource transform, RelativePoint transformOrigin, Size screenSize,
        AlignmentX alignmentX, AlignmentY alignmentY)
    {
        bool changed = false;
        if (Transform?.Resource.GetOriginal() != transform?.GetOriginal()
            || Transform?.Version != transform?.Version)
        {
            Transform = transform.Capture();
            changed = true;
        }

        if (TransformOrigin != transformOrigin)
        {
            TransformOrigin = transformOrigin;
            changed = true;
        }

        if (ScreenSize != screenSize)
        {
            ScreenSize = screenSize;
            changed = true;
        }

        if (AlignmentX != alignmentX)
        {
            AlignmentX = alignmentX;
            changed = true;
        }

        if (AlignmentY != alignmentY)
        {
            AlignmentY = alignmentY;
            changed = true;
        }

        HasChanges = changed;
        return changed;
    }

    private Matrix GetTransformMatrix(Rect bounds)
    {
        Vector pt = CalculateTranslate(bounds.Size);
        var origin = TransformOrigin.ToPixels(bounds.Size);
        Matrix offset = Matrix.CreateTranslation(origin);
        var transform = Transform?.Resource;

        if (transform != null)
        {
            return (-offset) * transform.Matrix * offset * Matrix.CreateTranslation(pt);
        }
        else
        {
            return Matrix.CreateTranslation(pt);
        }
    }

    private Point CalculateTranslate(Size bounds)
    {
        float x = 0;
        float y = 0;

        if (float.IsFinite(ScreenSize.Width))
        {
            switch (AlignmentX)
            {
                case Media.AlignmentX.Left:
                    x = 0;
                    break;
                case Media.AlignmentX.Center:
                    x = ScreenSize.Width / 2 - bounds.Width / 2;
                    break;
                case Media.AlignmentX.Right:
                    x = ScreenSize.Width - bounds.Width;
                    break;
            }
        }

        if (float.IsFinite(ScreenSize.Height))
        {
            switch (AlignmentY)
            {
                case Media.AlignmentY.Top:
                    y = 0;
                    break;
                case Media.AlignmentY.Center:
                    y = ScreenSize.Height / 2 - bounds.Height / 2;
                    break;
                case Media.AlignmentY.Bottom:
                    y = ScreenSize.Height - bounds.Height;
                    break;
            }
        }

        return new Point(x, y);
    }

    public override RenderNodeOperation[] Process(RenderNodeContext context)
    {
        var bounds = context.CalculateBounds();
        var transform = GetTransformMatrix(bounds);
        return context.Input.Select(r =>
            RenderNodeOperation.CreateLambda(
                r.Bounds.TransformToAABB(transform),
                canvas =>
                {
                    using (canvas.PushTransform(transform))
                    {
                        r.Render(canvas);
                    }
                },
                hitTest: point =>
                {
                    if (transform.HasInverse)
                        point *= transform.Invert();
                    return r.HitTest(point);
                },
                onDispose: r.Dispose))
            .ToArray();
    }
}
