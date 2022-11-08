using Beutl.Graphics.Filters;

namespace Beutl.Operators.Configure.ImageFilter;

public sealed class DropShadowOperator : ImageFilterOperator<DropShadow>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return DropShadow.PositionProperty;
        yield return DropShadow.SigmaProperty;
        yield return DropShadow.ColorProperty;
        yield return DropShadow.ShadowOnlyProperty;
    }
}
