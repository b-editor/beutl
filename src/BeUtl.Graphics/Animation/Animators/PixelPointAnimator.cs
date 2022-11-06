using Beutl.Media;

namespace Beutl.Animation.Animators;

public sealed class PixelPointAnimator : Animator<PixelPoint>
{
    public override PixelPoint Interpolate(float progress, PixelPoint oldValue, PixelPoint newValue)
    {
        int deltaX = newValue.X - oldValue.X;
        int deltaY = newValue.Y - oldValue.Y;

        float newX = (deltaX * progress) + oldValue.X;
        float newY = (deltaY * progress) + oldValue.Y;

        return new PixelPoint((int)MathF.Round(newX), (int)MathF.Round(newY));
    }
}
