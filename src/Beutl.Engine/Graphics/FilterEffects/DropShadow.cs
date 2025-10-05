using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed partial class DropShadow : FilterEffect
{
    public DropShadow()
    {
        ScanProperties<DropShadow>();
    }

    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    public IProperty<Point> Position { get; } = Property.CreateAnimatable(new Point());

    [Display(Name = nameof(Strings.Sigma), ResourceType = typeof(Strings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.Transparent);

    [Display(Name = nameof(Strings.ShadowOnly), ResourceType = typeof(Strings))]
    public IProperty<bool> ShadowOnly { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context)
    {
        if (ShadowOnly.CurrentValue)
        {
            context.DropShadowOnly(Position.CurrentValue, Sigma.CurrentValue, Color.CurrentValue);
        }
        else
        {
            context.DropShadow(Position.CurrentValue, Sigma.CurrentValue, Color.CurrentValue);
        }
    }

    public override Rect TransformBounds(Rect bounds)
    {
        Size sigma = Sigma.CurrentValue;
        Rect shadowBounds = bounds
            .Translate(Position.CurrentValue)
            .Inflate(new Thickness(sigma.Width * 3, sigma.Height * 3));

        return ShadowOnly.CurrentValue ? shadowBounds : bounds.Union(shadowBounds);
    }
}
