using Beutl.Graphics.Effects.OpenCv;
using Beutl.Media;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Effects;

public sealed class CvBlurOperator : FilterEffectOperator<Blur>
{
    public Setter<PixelSize> KernelSize { get; set; } = new(Blur.KernelSizeProperty, new(10, 10));

    public Setter<bool> FixImageSize { get; set; } = new(Blur.FixImageSizeProperty, false);
}
