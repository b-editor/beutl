using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(Strings.DropShadow), ResourceType = typeof(Strings))]
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

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.ShadowOnly)
        {
            context.DropShadowOnly(r.Position, r.Sigma, r.Color);
        }
        else
        {
            context.DropShadow(r.Position, r.Sigma, r.Color);
        }
    }
}
