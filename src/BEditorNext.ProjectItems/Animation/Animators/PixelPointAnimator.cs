using BEditorNext.Graphics;

namespace BEditorNext.Animation.Animators;

public sealed class PixelPointAnimator : Animator<PixelPoint>
{
    public override PixelPoint Interpolate(float progress, PixelPoint oldValue, PixelPoint newValue)
    {
        var deltaX = newValue.X - oldValue.X;
        var deltaY = newValue.Y - oldValue.Y;

        var newX = (deltaX * progress) + oldValue.X;
        var newY = (deltaY * progress) + oldValue.Y;

        return new PixelPoint((int)MathF.Round(newX), (int)MathF.Round(newY));
    }
}
