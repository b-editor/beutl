using Beutl;
using Beutl.Graphics.Effects;

namespace PackageSample;

public sealed class ChoicesProviderTest : FilterEffect
{
    public static readonly CoreProperty<WellKnownSize?> SizeProperty;
    private WellKnownSize? _size;

    static ChoicesProviderTest()
    {
        SizeProperty = ConfigureProperty<WellKnownSize?, ChoicesProviderTest>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .Register();

        AffectsRender<ChoicesProviderTest>(SizeProperty);
    }

    [ChoicesProvider(typeof(WellKnownSizesProvider))]
    public WellKnownSize? Size
    {
        get => _size;
        set => SetAndRaise(SizeProperty, ref _size, value);
    }

    public override void ApplyTo(FilterEffectContext context)
    {
    }
}
