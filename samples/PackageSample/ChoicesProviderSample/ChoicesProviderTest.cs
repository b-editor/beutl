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
    public IExpressionProperty<WellKnownSize?> Size { get; } = Property.CreateExpression<WellKnownSize?>();

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
        }
    }
}
