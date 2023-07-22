using Beutl.Graphics.Effects;

namespace Beutl.Operators.Configure.Effects;

public sealed class DropShadowOperator : FilterEffectOperator<DropShadow>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return DropShadow.PositionProperty;
        yield return DropShadow.SigmaProperty;
        yield return DropShadow.ColorProperty;
        yield return DropShadow.ShadowOnlyProperty;
    }
}
