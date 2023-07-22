using Beutl.Graphics.Effects.OpenCv;

namespace Beutl.Operators.Configure.Effects;

public sealed class CvBlurOperator : FilterEffectOperator<Blur>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return Blur.KernelSizeProperty;
        yield return Blur.FixImageSizeProperty;
    }
}
