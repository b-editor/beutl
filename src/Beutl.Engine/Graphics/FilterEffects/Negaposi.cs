using System.ComponentModel.DataAnnotations;

using Beutl.Engine;

namespace Beutl.Graphics.Effects;

public partial class Negaposi : FilterEffect
{
    public Negaposi()
    {
        ScanProperties<Negaposi>();
    }

    public IProperty<byte> Red { get; } = Property.CreateAnimatable<byte>();

    public IProperty<byte> Green { get; } = Property.CreateAnimatable<byte>();

    public IProperty<byte> Blue { get; } = Property.CreateAnimatable<byte>();

    [Range(0, 100)]
    public IProperty<float> Strength { get; } = Property.CreateAnimatable(100f);

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        var r = (Resource)resource;
        var color = (r.Red, r.Green, r.Blue);

        context.LookupTable(
            color,
            r.Strength / 100,
            static ((byte r, byte g, byte b) data, (byte[] A, byte[] R, byte[] G, byte[] B) array) =>
            {
                LookupTable.Linear(array.A);
                LookupTable.Negaposi((array.R, array.G, array.B), data.r, data.g, data.b);
            });
    }
}
