using Beutl.Media;

namespace Beutl.Animation.Animators;

public sealed class PixelRectAnimator : Animator<PixelRect>
{
    public override PixelRect Interpolate(float progress, PixelRect oldValue, PixelRect newValue)
    {
        var deltaX = newValue.X - oldValue.X;
        var deltaY = newValue.Y - oldValue.Y;
        var deltaWidth = newValue.Width - oldValue.Width;
        var deltaHeight = newValue.Height - oldValue.Height;

        var newX = (deltaX * progress) + oldValue.X;
        var newY = (deltaY * progress) + oldValue.Y;
        var newWidth = (deltaWidth * progress) + oldValue.Width;
        var newHeight = (deltaHeight * progress) + oldValue.Height;

        return new PixelRect(
            (int)MathF.Round(newX),
            (int)MathF.Round(newY),
            (int)MathF.Round(newWidth),
            (int)MathF.Round(newHeight));
    }
}
