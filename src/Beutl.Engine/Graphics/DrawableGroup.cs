using System.ComponentModel.DataAnnotations;
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

    public new partial class Resource
    {
        private readonly FlowChildren _children = new(keepsFlowInputs: true);

        internal override IReadOnlyList<FlowNode> FlowInputs => _children.FlowInputs;

        internal override EngineObject? FlowOwner => _children.FlowOwner;

        public List<Drawable.Resource> Children
        {
            get => _children.Items;
            set => _children.Items = value;
        }

        protected override void RenderCore(GraphicsContext2D context)
        {
            Size availableSize = context.Size;
            var boundsMemory = context.UseMemory<Rect>();
            var transformParams = (Transform, TransformOrigin, availableSize, boundsMemory);
            bool isolatesContent = Opacity != 100f
                                   || EvaluatedBlendMode != Graphics.BlendMode.SrcOver
                                   || Children.Any(static child => child.BlendMode != Graphics.BlendMode.SrcOver);

            using (context.PushBlendMode(BlendMode))
            using (PushCustomTransform(context, transformParams))
            using (context.PushOpacity(Opacity / 100f))
            using (ContentIsolationRenderNode.Push(context, isolatesContent))
            using (FilterEffect == null ? new() : context.PushFilterEffect(FilterEffect))
            using (PushContentBounds(context, boundsMemory))
            {
                OnDraw(context);
            }
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
            foreach (Drawable.Resource item in Children)
            {
                context.DrawDrawable(item);
            }
        }

        protected override Size MeasureCore(Size availableSize)
        {
            return Size.Empty;
        }

        partial void PreReconcile(DrawableGroup obj, CompositionContext context)
        {
            if (_children.Reconcile(context, obj.Children, obj))
                Version++;
        }

        partial void PostDispose(bool disposing)
        {
            _children.Dispose();
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

        // The matrix the last render placed the content with, from the bounds it measured.
        internal Matrix Matrix => GetTransformMatrix(Bounds.Value);

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
