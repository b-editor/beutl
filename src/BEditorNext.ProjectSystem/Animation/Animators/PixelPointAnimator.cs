using BEditorNext.Media;

namespace BEditorNext.Animation.Animators;

public sealed class PixelPointAnimator : Animator<PixelPoint>
{
    public override PixelPoint Multiply(PixelPoint left, float right)
    {
        return new PixelPoint((int)MathF.Round(left.X * right), (int)MathF.Round(left.Y * right));
    }
}
