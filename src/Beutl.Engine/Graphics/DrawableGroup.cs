using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics;

[Display(Name = nameof(Strings.Group), ResourceType = typeof(Strings))]
public sealed partial class DrawableGroup : Drawable
{
    public DrawableGroup()
    {
        ScanProperties<DrawableGroup>();
    }

    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (resource.IsEnabled)
        {
            var r = (Resource)resource;
            Size availableSize = context.Size.ToSize(1);
            var boundsMemory = context.UseMemory<Rect>();
            var transformParams = (r.Transform, r.TransformOrigin, availableSize, boundsMemory);

            using (context.PushBlendMode(r.BlendMode))
            using (context.PushNode(
                       transformParams,
                       b => new CustomTransformRenderNode(
                           b.Transform, b.TransformOrigin, b.availableSize,
                           Media.AlignmentX.Left, Media.AlignmentY.Top, b.boundsMemory),
                       (n, b) => n.Update(
                           b.Transform, b.TransformOrigin, b.availableSize,
                           Media.AlignmentX.Left, Media.AlignmentY.Top, b.boundsMemory)))
            using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
            using (context.PushNode(
                       boundsMemory,
                       b => new BoundsObserveNode(b),
                       (n, b) => n.Update(b)))
            {
                OnDraw(context, r);
            }
        }
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        foreach (Drawable.Resource item in r.Children)
        {
            context.DrawDrawable(item);
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        return Size.Empty;
    }

    internal sealed class BoundsObserveNode : ContainerRenderNode
    {
        public BoundsObserveNode(MemoryNode<Rect> memoryNode)
        {
            MemoryNode = memoryNode;
        }

        public MemoryNode<Rect> MemoryNode { get; private set; }

        public bool Update(MemoryNode<Rect> memoryNode)
        {
            if (memoryNode != MemoryNode)
            {
                MemoryNode = memoryNode;
                HasChanges = true;
                return true;
            }

            return false;
        }

        public override RenderNodeOperation[] Process(RenderNodeContext context)
        {
            MemoryNode.Value = context.CalculateBounds();
            return context.Input;
        }
    }

    internal sealed class CustomTransformRenderNode(
        Transform.Resource? transform,
        RelativePoint transformOrigin,
        Size screenSize,
        AlignmentX alignmentX,
        AlignmentY alignmentY,
        MemoryNode<Rect> bounds) : ContainerRenderNode
    {
        public (Transform.Resource Resource, int Version)? Transform { get; private set; } = transform.Capture();

        public RelativePoint TransformOrigin { get; private set; } = transformOrigin;

        public Size ScreenSize { get; private set; } = screenSize;

        public AlignmentX AlignmentX { get; private set; } = alignmentX;

        public AlignmentY AlignmentY { get; private set; } = alignmentY;

        public MemoryNode<Rect> Bounds { get; private set; } = bounds;

        public bool Update(
            Transform.Resource? transform, RelativePoint transformOrigin, Size screenSize,
            AlignmentX alignmentX, AlignmentY alignmentY, MemoryNode<Rect> bounds)
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

            if (Bounds != bounds)
            {
                Bounds = bounds;
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
            var bounds = Bounds.Value;
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
