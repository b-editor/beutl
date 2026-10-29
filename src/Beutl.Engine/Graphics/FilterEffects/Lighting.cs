using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.Lighting), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Lighting : FilterEffect
{
    public Lighting()
    {
        ScanProperties<Lighting>();
    }

    [Display(Name = nameof(GraphicsStrings.Lighting_Multiply), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Multiply { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(GraphicsStrings.Lighting_Add), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Color> Add { get; } = Property.CreateAnimatable(Colors.Black);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.Lighting(Multiply, Add);
        }
    }
}
