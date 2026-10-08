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
            bool isolatesContent = resource.Opacity != 100f
                                   || r.BlendMode != Graphics.BlendMode.SrcOver
                                   || r.Children.Any(static child => child.BlendMode != Graphics.BlendMode.SrcOver);

            using (context.PushBlendMode(r.BlendMode))
            using (PushCustomTransform(context, transformParams))
            using (context.PushOpacity(resource.Opacity / 100f))
            using (context.PushNode(
                       isolatesContent,
                       b => new ContentIsolationRenderNode(b),
                       (n, b) => n.Update(b)))
            using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
            using (PushContentBounds(context, boundsMemory))
            {
                OnDraw(context, r);
            }
        }
    }

    /// <summary>
    /// Pushes the node that applies a container's transform to its content, laid out from the top-left corner.
    /// </summary>
    internal static PushedState PushCustomTransform(
        GraphicsContext2D context,
        in (Transform.Resource? Transform, RelativePoint TransformOrigin, Size availableSize, MemoryNode<Rect> boundsMemory) transformParams)
        => context.PushNode(
            transformParams,
            b => new CustomTransformRenderNode(
                b.Transform, b.TransformOrigin, b.availableSize,
                Media.AlignmentX.Left, Media.AlignmentY.Top, b.boundsMemory),
            (n, b) => n.Update(
                b.Transform, b.TransformOrigin, b.availableSize,
                Media.AlignmentX.Left, Media.AlignmentY.Top, b.boundsMemory));

    /// <summary>Pushes the node that records the bounds of the content drawn inside it.</summary>
    internal static PushedState PushContentBounds(GraphicsContext2D context, MemoryNode<Rect> boundsMemory)
        => context.PushNode(
            boundsMemory,
            b => new ContentBoundsRenderNode(b),
            (n, b) => n.Update(b));

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
        private readonly PooledList<int> _childrenVersion = [];
        private readonly FlowInputState _flowInputs = new();

        internal override IReadOnlyList<FlowNode> FlowInputs => _flowInputs.Inputs;

        public List<Drawable.Resource> Children { get; set; } = [];

        partial void PreUpdate(DrawableGroup obj, CompositionContext context)
        {
            if (ResourceReconciler.ReconcileChildrenFromFlow(context, obj.Children, Children, _childrenVersion, obj, _flowInputs))
                Version++;
        }

        partial void PostDispose(bool disposing)
        {
            ResourceReconciler.ReleaseReconciledChildren(Children, _childrenVersion);
            _flowInputs.Dispose();
        }
    }

    internal sealed class ContentBoundsRenderNode(MemoryNode<Rect> memoryNode) : ContainerRenderNode
    {
        public MemoryNode<Rect> MemoryNode { get; private set; } = memoryNode;

        public bool Update(MemoryNode<Rect> memoryNode)
        {
            if (memoryNode != MemoryNode)
            {
                MemoryNode = memoryNode;
                MarkChanged();
                return true;
            }

            return false;
        }

        public override void Process(RenderNodeContext context)
        {
            MemoryNode.Value = context.CalculateRecordedInputBoundsHint();
            context.PassThrough();
        }
    }

    internal sealed class ContentIsolationRenderNode(bool isolatesContent) : ContainerRenderNode
    {
        public bool IsolatesContent { get; private set; } = isolatesContent;

        public bool Update(bool isolatesContent)
        {
            if (isolatesContent != IsolatesContent)
            {
                IsolatesContent = isolatesContent;
                MarkChanged();
                return true;
            }

            return false;
        }

        public override void Process(RenderNodeContext context)
        {
            if (IsolatesContent)
            {
                // A full-target write in the group - a clear, an opaque raw command - has no recorded value
                // bounds, so scoping by them would make the isolation scope empty and drop the group's whole
                // contribution instead of compositing it.
                TargetRegion region = context.HasSymbolicInputTargetWrite()
                    ? TargetRegion.Full
                    : TargetRegion.Region(context.CalculateRecordedInputBoundsHint());
                context.Publish(context.TargetLayerScope(context.Inputs, region));
            }
            else
            {
                context.PassThrough();
            }
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

            if (changed)
            {
                MarkChanged();
            }

            return changed;
        }

        private Matrix GetTransformMatrix(Rect bounds)
        {
            Vector pt = CalculateAlignmentTranslate(AlignmentX, AlignmentY, bounds.Size, ScreenSize);
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

        public override void Process(RenderNodeContext context)
        {
            Matrix transform = GetTransformMatrix(Bounds.Value);
            // Declared rather than hand-built so this scope joins the ambient every Append and Set below it
            // composes against: its matrix comes from measured content, so nothing above can predict it.
            context.PublishMappedInputs(
                RenderScopeAmbientTransform.CreateScope(
                    new RenderScopeAmbientTransform(transform, TransformOperator.Prepend, context.TargetDomain),
                    transform,
                    capturesBackingTarget: true),
                static (context, input, value) => context.TargetScope(input, value));
        }
    }
}
