using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics.Effects;

public sealed partial class HueRotate : FilterEffect
{
    public HueRotate()
    {
        ScanProperties<HueRotate>();
    }

    [Display(Name = nameof(Strings.Angle), ResourceType = typeof(Strings))]
    public IProperty<float> Angle { get; } = Property.CreateAnimatable<float>();

    public override void ApplyTo(FilterEffectContext context)
    {
        context.HueRotate(Angle.CurrentValue);
    }
}
