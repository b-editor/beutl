using Beutl.Graphics.Effects;

namespace Beutl.Operators.Configure.Effects;

public sealed class InnerShadowOperator : FilterEffectOperator<InnerShadow>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return InnerShadow.PositionProperty;
        yield return InnerShadow.SigmaProperty;
        yield return InnerShadow.ColorProperty;
        yield return InnerShadow.ShadowOnlyProperty;
    }
}
