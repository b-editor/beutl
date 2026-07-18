using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics;

[Display(Name = nameof(GraphicsStrings.Group), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DrawableGroup : Drawable, IFlowOperator
{
    public DrawableGroup()
    {
        ScanProperties<DrawableGroup>();
        HideProperties(AlignmentX, AlignmentY);
    }

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.Children), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        if (resource.IsEnabled)
        {
            var r = (Resource)resource;
            Size availableSize = context.Size;
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
            using (context.PushOpacity(resource.Opacity / 100f))
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

    public new partial class Resource
    {
        private static readonly ReadOnlyCollection<Drawable.Resource> s_emptyChildren
            = Array.AsReadOnly(Array.Empty<Drawable.Resource>());

        private readonly List<Drawable.Resource> _children = [];
        private readonly PooledList<int> _childrenVersion = [];
        private IReadOnlyList<Drawable.Resource> _childrenSnapshot = s_emptyChildren;

        public IReadOnlyList<Drawable.Resource> Children => ReadGeneratedResourceState(ref _childrenSnapshot);

        partial void PreUpdate(DrawableGroup obj, CompositionContext context)
        {
            EngineObject.Resource[]? flowRollbackSnapshot = context.Flow?.ToArray();
            // Consume all Drawables from flow
            using var consumed = new PooledList<Drawable.Resource>();
            if (context.Flow != null)
            {
                for (int i = context.Flow.Count - 1; i >= 0; i--)
                {
                    if (context.Flow[i] is Drawable.Resource d)
                    {
                        consumed.Insert(0, d);
                        context.Flow.RemoveAt(i);
                    }
                }
            }

            // Reconcile children from consumed drawables
            bool changed = false;
            try
            {
                ResourceReconciler.ReconcileListFromFlow(
                    context: context,
                    property: obj.Children,
                    consumed: consumed,
                    field: _children,
                    versions: _childrenVersion,
                    flowRollbackSnapshot: flowRollbackSnapshot,
                    changed: ref changed);
            }
            finally
            {
                PublishChildrenSnapshotIfChanged();
                if (changed)
                    Version++;
            }
        }

        partial void PostUpdate(DrawableGroup obj, CompositionContext context)
        {
            PublishChildrenSnapshotIfChanged();
        }

        partial void PrepareResourceDispose(
            bool disposing,
            EngineObject.Resource.GeneratedResourceCleanupContext context)
        {
            if (!disposing)
                return;

            int ownedStart = Math.Min(_childrenVersion.Count, _children.Count);
            for (int i = ownedStart; i < _children.Count; i++)
            {
                context.Reserve(_children[i]);
            }
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            Exception? failure = null;
            _children.Clear();
            Volatile.Write(ref _childrenSnapshot, s_emptyChildren);
            try
            {
                _childrenVersion.Dispose();
            }
            catch (Exception ex)
            {
                failure ??= ex;
            }

            ThrowIfCleanupFailed(failure);
        }

        private void PublishChildrenSnapshotIfChanged()
        {
            IReadOnlyList<Drawable.Resource> current = Volatile.Read(ref _childrenSnapshot);
            if (current.Count == _children.Count)
            {
                bool matches = true;
                for (int i = 0; i < _children.Count; i++)
                {
                    if (!ReferenceEquals(current[i], _children[i]))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    return;
            }

            IReadOnlyList<Drawable.Resource> next = _children.Count == 0
                ? s_emptyChildren
                : Array.AsReadOnly(_children.ToArray());
            Volatile.Write(ref _childrenSnapshot, next);
        }
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
                        onDispose: r.Dispose,
                        // Re-scale a bitmap child's supply density through the transform boundary.
                        effectiveScale: TransformRenderNode.RescaleDensity(r.EffectiveScale, transform)))
                .ToArray();
        }
    }
}
