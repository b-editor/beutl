using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;

namespace Beutl.Graphics;

/// <summary>
/// Averages explicit time samples of upstream drawables, including their transforms, effects and media.
/// This operator's own transform and effects apply once, after the temporal average.
/// </summary>
[Display(Name = nameof(GraphicsStrings.MotionBlur), Description = nameof(GraphicsStrings.MotionBlur_Description),
    ResourceType = typeof(GraphicsStrings))]
public sealed partial class MotionBlur : Drawable, IFlowOperator
{
    public MotionBlur()
    {
        ScanProperties<MotionBlur>();
        HideProperties(AlignmentX, AlignmentY);
    }

    [Display(Name = nameof(GraphicsStrings.MotionBlur_ShutterAngle), Description = nameof(GraphicsStrings.MotionBlur_ShutterAngle_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(0, 360)]
    public IExpressionProperty<float> ShutterAngle { get; } = Property.CreateAnimatable(180f);

    [Display(Name = nameof(GraphicsStrings.MotionBlur_ShutterPosition), Description = nameof(GraphicsStrings.MotionBlur_ShutterPosition_Description),
        ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ShutterPosition> ShutterPosition { get; } = Property.CreateExpression(Graphics.ShutterPosition.Center);

    [Display(Name = nameof(GraphicsStrings.MotionBlur_Samples), Description = nameof(GraphicsStrings.MotionBlur_Samples_Description),
        ResourceType = typeof(GraphicsStrings))]
    [Range(1, 64)]
    public IExpressionProperty<int> Samples { get; } = Property.CreateExpression(8);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.Children), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<Drawable> Children { get; } = Property.CreateList<Drawable>();

    public override void Render(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (!r.IsEnabled) return;
        var bounds = context.UseMemory<Rect>();
        using (context.PushBlendMode(r.BlendMode))
        using (DrawableGroup.PushCustomTransform(context, (r.Transform, r.TransformOrigin, context.Size, bounds)))
        using (context.PushOpacity(r.Opacity / 100f))
        using (r.FilterEffect == null ? new() : context.PushFilterEffect(r.FilterEffect))
        using (DrawableGroup.PushContentBounds(context, bounds))
        {
            r.Sampling.Draw(context, r.CancellationToken);
        }
    }

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource) => Size.Empty;

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
    }

    public new partial class Resource
    {
        private readonly FlowChildren _children = new(keepsFlowInputs: true);
        internal readonly TemporalSampling Sampling = new();
        internal CancellationToken CancellationToken;
        internal override IReadOnlyList<FlowNode> FlowInputs => _children.FlowInputs;

        partial void PreReconcile(MotionBlur obj, CompositionContext context)
        {
            if (_children.Reconcile(context, obj.Children, obj)) Version++;
        }

        partial void PostReconcile(MotionBlur obj, CompositionContext context)
        {
            CancellationToken = context.CancellationToken;
            try
            {
                if (Sampling.Reconcile(context, _children.Items, ShutterAngle, ShutterPosition, Samples)) Version++;
            }
            catch
            {
                // A cancelled evaluation may have updated some slots; a retry must re-record the frame.
                Version++;
                throw;
            }
        }

        partial void PostDispose(bool disposing)
        {
            Sampling.Dispose();
            _children.Dispose();
        }
    }
}
