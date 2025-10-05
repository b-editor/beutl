using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Effects;

public sealed partial class Lighting : FilterEffect
{
    public Lighting()
    {
        ScanProperties<Lighting>();
    }

    [Display(Name = nameof(Strings.Multiplication), ResourceType = typeof(Strings))]
    public IProperty<Color> Multiply { get; } = Property.CreateAnimatable(Colors.White);

    [Display(Name = nameof(Strings.Addition), ResourceType = typeof(Strings))]
    public IProperty<Color> Add { get; } = Property.CreateAnimatable<Color>();

    public override void ApplyTo(FilterEffectContext context)
    {
        context.Lighting(Multiply.CurrentValue, Add.CurrentValue);
    }
}
