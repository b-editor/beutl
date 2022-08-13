using BeUtl.Graphics.Effects.OpenCv;

namespace BeUtl.Operators.Configure.BitmapEffect;

public sealed class BlurOperator : BitmapEffectOperator<Blur>
{
    protected override IEnumerable<CoreProperty> GetProperties()
    {
        yield return Blur.KernelSizeProperty;
        yield return Blur.FixImageSizeProperty;
    }
}
