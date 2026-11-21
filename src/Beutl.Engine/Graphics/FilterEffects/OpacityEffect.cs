using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

[Display(Name = nameof(GraphicsStrings.OpacityEffect), ResourceType = typeof(GraphicsStrings))]
public sealed partial class OpacityEffect : FilterEffect
{
    public OpacityEffect()
    {
        ScanProperties<OpacityEffect>();
    }

    [Display(Name = nameof(GraphicsStrings.Opacity), ResourceType = typeof(GraphicsStrings))]
    [Range(0, 100)]
    [Unit(PropertyUnit.Percent)]
    public IExpressionProperty<float> Opacity { get; } = Property.CreateAnimatable(100f);

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            if (!float.IsFinite(Opacity) || Opacity >= 100)
                return;

            context.Opacity(Math.Max(Opacity, 0) / 100f);
        }
    }
}
