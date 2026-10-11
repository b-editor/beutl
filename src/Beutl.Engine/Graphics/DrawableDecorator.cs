using System.ComponentModel.DataAnnotations;
using Beutl.Collections.Pooled;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics;

// Drawable継承しているが、Drawableのメソッドは使っていない
[Display(Name = nameof(GraphicsStrings.DrawableDecorator), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DrawableDecorator : Drawable, IFlowOperator
{
    public DrawableDecorator()
    {
        ScanProperties<DrawableDecorator>();
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

            foreach (var child in r.Children)
            {
                // Replayed bounds nodes do not write again, so each child must retain its own bounds.
                var boundsMemory = context.UseMemory<Rect>();
                var transformParams = (r.Transform, r.TransformOrigin, availableSize, boundsMemory);

                using (context.PushBlendMode(r.BlendMode))
                using (DrawableGroup.PushCustomTransform(context, transformParams))
                using (context.PushOpacity(resource.Opacity / 100f))
                using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
                using (DrawableGroup.PushContentBounds(context, boundsMemory))
                {
                    context.DrawDrawable(child);
                }
            }
        }
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        return Size.Empty;
    }

    public new partial class Resource
    {
        private readonly PooledList<int> _childrenVersion = [];
        private readonly FlowInputState _flowInputs = new();
        private List<Drawable.Resource> _children = [];

        internal override IReadOnlyList<FlowNode> FlowInputs => _flowInputs.Inputs;

        public List<Drawable.Resource> Children
        {
            get => _children;
            set => _children = value;
        }

        partial void PostReconcile(DrawableDecorator obj, CompositionContext context)
        {
            if (ResourceReconciler.ReconcileChildrenFromFlow(context, obj.Children, _children, _childrenVersion, obj, _flowInputs))
                Version++;
        }

        partial void PostDispose(bool disposing)
        {
            ResourceReconciler.ReleaseReconciledChildren(_children, _childrenVersion);
            _flowInputs.Dispose();
        }
    }
}
