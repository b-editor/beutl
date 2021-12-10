using BEditorNext.Media;

namespace BEditorNext.Animation.Animators;

public sealed class PixelSizeAnimator : Animator<PixelSize>
{
    public override PixelSize Multiply(PixelSize left, float right)
    {
        int width = (int)MathF.Round(left.Width * right);
        int height = (int)MathF.Round(left.Height * right);
        return new PixelSize(width, height);
    }
}
