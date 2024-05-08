using System.ComponentModel.DataAnnotations;
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

    protected override void OnDraw(ICanvas canvas)
    {
    }

    public override void Render(ICanvas canvas)
    {
        base.Render(canvas);

        if (IsVisible)
        {
            var backdrop = canvas.Snapshot();
            if (Clear)
            {
                canvas.Clear();
            }

            Size availableSize = canvas.Size.ToSize(1);
            Size size = MeasureCore(availableSize);
            var rect = new Rect(size);
            if (FilterEffect != null)
            {
                rect = FilterEffect.TransformBounds(rect);
            }

            Matrix transform = GetTransformMatrix(availableSize, size);
            Rect transformedBounds = rect.TransformToAABB(transform);
            using (canvas.PushBlendMode(BlendMode))
            using (canvas.PushTransform(transform))
            using (FilterEffect == null ? new() : canvas.PushFilterEffect(FilterEffect))
            using (OpacityMask == null ? new() : canvas.PushOpacityMask(OpacityMask, new Rect(size)))
            {
                canvas.DrawBackdrop(backdrop);
            }

            Bounds = transformedBounds;
        }
    }
}
