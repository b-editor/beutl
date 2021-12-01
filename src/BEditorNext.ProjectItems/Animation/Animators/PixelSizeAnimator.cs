using BEditorNext.Graphics;

namespace BEditorNext.Animation.Animators;

public sealed class PixelSizeAnimator : Animator<PixelSize>
{
    public override PixelSize Interpolate(float progress, PixelSize oldValue, PixelSize newValue)
    {
        var deltaWidth = newValue.Width - oldValue.Width;
        var deltaHeight = newValue.Height - oldValue.Height;

        var newWidth = (deltaWidth * progress) + oldValue.Width;
        var newHeight = (deltaHeight * progress) + oldValue.Height;

        return new PixelSize((int)MathF.Round(newWidth), (int)MathF.Round(newHeight));
    }
}
