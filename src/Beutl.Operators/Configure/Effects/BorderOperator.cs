using Beutl.Graphics.Effects;

namespace Beutl.Operators.Configure.Effects;

public sealed class BorderOperator : FilterEffectOperator<Border>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return Border.OffsetProperty;
        yield return Border.ThicknessProperty;
        yield return Border.ColorProperty;
        yield return Border.MaskTypeProperty;
        yield return Border.StyleProperty;
    }
}
