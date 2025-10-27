using Beutl.Engine;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

// TODO: 実験的
public sealed class BoundaryTransformRenderNode(
    Transform.Resource? transform,
    RelativePoint transformOrigin,
    Size screenSize,
    AlignmentX alignmentX,
    AlignmentY alignmentY,
    bool splitted) : ContainerRenderNode
{
    public (Transform.Resource Resource, int Version)? Transform { get; private set; } = transform.Capture();

    public RelativePoint TransformOrigin { get; private set; } = transformOrigin;

    public Size ScreenSize { get; private set; } = screenSize;

    public AlignmentX AlignmentX { get; private set; } = alignmentX;

    public AlignmentY AlignmentY { get; private set; } = alignmentY;

    public bool Splitted { get; private set; } = splitted;

    public bool Update(
        Transform.Resource? transform, RelativePoint transformOrigin, Size screenSize,
        AlignmentX alignmentX, AlignmentY alignmentY, bool splitted)
    {
        bool changed = false;
        if (!transform.Compare(Transform))
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

        if (Splitted != splitted)
        {
            Splitted = splitted;
            changed = true;
        }

        HasChanges = changed;
        return changed;
    }

    private Matrix GetTransformMatrix(Rect bounds)
    {
        Vector pt = CalculateTranslate(bounds.Size);
        var origin = TransformOrigin.ToPixels(bounds.Size);
        Matrix offset = Matrix.CreateTranslation(origin + bounds.Position);
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
        if (Splitted)
        {

            return context.Input.Select(r =>
            {
                var bounds = r.Bounds;
                var transform = GetTransformMatrix(bounds);
                return RenderNodeOperation.CreateLambda(
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
                    onDispose: r.Dispose);
            })
                .ToArray();
        }
        else
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
}
