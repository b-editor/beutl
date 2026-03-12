using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.InnerShadow), ResourceType = typeof(GraphicsStrings))]
public partial class InnerShadow : FilterEffect
{
    public InnerShadow()
    {
        ScanProperties<InnerShadow>();
    }

    [Display(Name = nameof(GraphicsStrings.InnerShadow_Position), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> Position { get; } = Property.CreateAnimatable(new Point());

    [Display(Name = nameof(GraphicsStrings.InnerShadow_Sigma), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public IProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    [Display(Name = nameof(GraphicsStrings.InnerShadow_Color), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.Transparent);

    [Display(Name = nameof(GraphicsStrings.InnerShadow_ShadowOnly), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> ShadowOnly { get; } = Property.CreateAnimatable(false);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        if (r.ShadowOnly)
        {
            context.InnerShadowOnly(r.Position, r.Sigma, r.Color);
        }
        else
        {
            context.InnerShadow(r.Position, r.Sigma, r.Color);
        }
    }
}
