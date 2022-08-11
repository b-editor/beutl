using BeUtl.Graphics.Filters;

namespace BeUtl.Operators.Configure.ImageFilter;

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
