using BeUtl.Graphics.Effects;

namespace BeUtl.Operators.Configure.BitmapEffect;

public sealed class InnerShadowOperator : BitmapEffectOperator<InnerShadow>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return InnerShadow.PositionProperty;
        yield return InnerShadow.KernelSizeProperty;
        yield return InnerShadow.ColorProperty;
    }
}
