using Beutl.Graphics;

namespace Beutl.Animation.Animators;

public sealed class SizeAnimator : Animator<Size>
{
    public override Size Interpolate(float progress, Size oldValue, Size newValue)
    {
        var deltaWidth = newValue.Width - oldValue.Width;
        var deltaHeight = newValue.Height - oldValue.Height;

        var newWidth = (deltaWidth * progress) + oldValue.Width;
        var newHeight = (deltaHeight * progress) + oldValue.Height;

        return new Size(newWidth, newHeight);
    }
}
