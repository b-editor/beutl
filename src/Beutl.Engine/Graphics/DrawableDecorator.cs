using System.ComponentModel.DataAnnotations;
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

    public new partial class Resource
    {
        private readonly FlowChildren _children = new(keepsFlowInputs: true);

        internal override IReadOnlyList<FlowNode> FlowInputs => _children.FlowInputs;

        public List<Drawable.Resource> Children
        {
            get => _children.Items;
            set => _children.Items = value;
        }

        protected override void RenderCore(GraphicsContext2D context)
        {
            Size availableSize = context.Size;

            foreach (var child in Children)
            {
                // Replayed bounds nodes do not write again, so each child must retain its own bounds.
                var boundsMemory = context.UseMemory<Rect>();
                var transformParams = (Transform, TransformOrigin, availableSize, boundsMemory);

                using (context.PushBlendMode(BlendMode))
                using (DrawableGroup.PushCustomTransform(context, transformParams))
                using (context.PushOpacity(Opacity / 100f))
                using (FilterEffect == null ? new() : context.PushFilterEffect(FilterEffect))
                using (DrawableGroup.PushContentBounds(context, boundsMemory))
                {
                    context.DrawDrawable(child);
                }
            }
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
        }

        protected override Size MeasureCore(Size availableSize)
        {
            return Size.Empty;
        }

        partial void PostReconcile(DrawableDecorator obj, CompositionContext context)
        {
            if (_children.Reconcile(context, obj.Children, obj))
                Version++;
        }

        partial void PostDispose(bool disposing)
        {
            _children.Dispose();
        }
    }
}
