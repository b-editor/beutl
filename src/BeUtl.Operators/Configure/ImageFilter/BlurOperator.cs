using Beutl.Graphics.Filters;

namespace Beutl.Operators.Configure.ImageFilter;

public sealed class BlurOperator : ImageFilterOperator<Blur>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return Blur.SigmaProperty;
    }
}
