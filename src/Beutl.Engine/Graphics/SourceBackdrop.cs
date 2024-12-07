using System.ComponentModel.DataAnnotations;
using Beutl.Graphics.Rendering.V2;
using Beutl.Language;

namespace Beutl.Graphics;

public class SourceBackdrop : Drawable
{
    public static readonly CoreProperty<bool> ClearProperty;
    private bool _clear;

    static SourceBackdrop()
    {
        ClearProperty = ConfigureProperty<bool, SourceBackdrop>(nameof(Clear))
            .Accessor(o => o.Clear, (o, v) => o.Clear = v)
            .DefaultValue(false)
            .Register();

        AffectsRender<SourceBackdrop>(ClearProperty);
    }

    [Display(Name = nameof(Strings.ClearBuffer), ResourceType = typeof(Strings))]
    public bool Clear
    {
        get => _clear;
        set => SetAndRaise(ClearProperty, ref _clear, value);
    }

    protected override Size MeasureCore(Size availableSize)
    {
        return availableSize;
    }

    protected override void OnDraw(GraphicsContext2D context)
    {
    }

    public override void Render(GraphicsContext2D context)
    {
        base.Render(context);

        if (IsVisible)
        {
            var backdrop = context.Snapshot();
            if (Clear)
            {
                context.Clear();
            }

            Size availableSize = context.Size.ToSize(1);
            Size size = MeasureCore(availableSize);
            var rect = new Rect(size);
            if (FilterEffect != null)
            {
                rect = FilterEffect.TransformBounds(rect);
            }

            Matrix transform = GetTransformMatrix(availableSize, size);
            Rect transformedBounds = rect.TransformToAABB(transform);
            using (context.PushBlendMode(BlendMode))
            using (context.PushTransform(transform))
            using (FilterEffect == null ? new() : context.PushFilterEffect(FilterEffect))
            using (OpacityMask == null ? new() : context.PushOpacityMask(OpacityMask, new Rect(size)))
            {
                context.DrawBackdrop(backdrop);
            }

            Bounds = transformedBounds;
        }
    }
}
