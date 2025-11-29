using Beutl;
using Beutl.Engine;
using Beutl.Graphics.Effects;

namespace PackageSample;

public sealed partial class ChoicesProviderTest : FilterEffect
{
    public ChoicesProviderTest()
    {
        ScanProperties<ChoicesProviderTest>();
    }

    [ChoicesProvider(typeof(WellKnownSizesProvider))]
    public IProperty<WellKnownSize?> Size { get; } = Property.Create<WellKnownSize?>();

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
    }
}
