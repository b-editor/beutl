using Beutl.Graphics;
using Beutl.Graphics.Effects.OpenCv;
using Beutl.Media;
using Beutl.Styling;

namespace Beutl.Operators.Configure.Effects;

public sealed class CvBlursOperator : FilterEffectOperator<Blur>
{
    public Setter<PixelSize> KernelSize { get; set; } = new(Blur.KernelSizeProperty, new(10, 10));

    public Setter<bool> FixImageSize { get; set; } = new(Blur.FixImageSizeProperty, false);
}

public sealed class CvGaussianBlurOperator : FilterEffectOperator<GaussianBlur>
{
    public Setter<PixelSize> KernelSize { get; set; } = new(GaussianBlur.KernelSizeProperty, new(10, 10));

    public Setter<Size> Sigma { get; set; } = new(GaussianBlur.SigmaProperty, new(0, 0));

    public Setter<bool> FixImageSize { get; set; } = new(GaussianBlur.FixImageSizeProperty, false);
}

public sealed class CvMedianBlurOperator : FilterEffectOperator<MedianBlur>
{
    public Setter<int> KernelSize { get; set; } = new(MedianBlur.KernelSizeProperty, 10);

    public Setter<bool> FixImageSize { get; set; } = new(MedianBlur.FixImageSizeProperty, false);
}
