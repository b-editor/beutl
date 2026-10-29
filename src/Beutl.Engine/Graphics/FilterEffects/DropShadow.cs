using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.DropShadow), ResourceType = typeof(GraphicsStrings))]
public sealed partial class DropShadow : FilterEffect
{
    public DropShadow()
    {
        ScanProperties<DropShadow>();
    }

    [Display(Name = nameof(GraphicsStrings.Position), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Point> Position { get; } = Property.CreateAnimatable(new Point());

    [Display(Name = nameof(GraphicsStrings.Sigma), ResourceType = typeof(GraphicsStrings))]
    [Range(typeof(Size), "0,0", "max,max")]
    public IExpressionProperty<Size> Sigma { get; } = Property.CreateAnimatable(Size.Empty);

    [Display(Name = nameof(GraphicsStrings.Color), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.ShadowOnly), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> ShadowOnly { get; } = Property.CreateAnimatable(false);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (ShadowOnly)
            {
                context.DropShadowOnly(Position, Sigma, Color);
            }
            else
            {
                context.DropShadow(Position, Sigma, Color);
            }
        }
    }
}
