using BEditorNext.Media;

namespace BEditorNext.Animation.Animators;

public sealed class PixelRectAnimator : Animator<PixelRect>
{
    public override PixelRect Multiply(PixelRect left, float right)
    {
        return new PixelRect(
            (int)MathF.Round(left.X * right),
            (int)MathF.Round(left.Y * right),
            (int)MathF.Round(left.Width * right),
            (int)MathF.Round(left.Height * right));
    }
}
