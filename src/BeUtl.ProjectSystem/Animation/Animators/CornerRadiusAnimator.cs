using BeUtl.Media;

namespace BeUtl.Animation.Animators;

public sealed class CornerRadiusAnimator : Animator<CornerRadius>
{
    public override CornerRadius Interpolate(float progress, CornerRadius oldValue, CornerRadius newValue)
    {
        float deltaTL = newValue.TopLeft - oldValue.TopLeft;
        float deltaTR = newValue.TopRight - oldValue.TopRight;
        float deltaBR = newValue.BottomRight - oldValue.BottomRight;
        float deltaBL = newValue.BottomLeft - oldValue.BottomLeft;

        float nTL = progress * deltaTL + oldValue.TopLeft;
        float nTR = progress * deltaTR + oldValue.TopRight;
        float nBR = progress * deltaBR + oldValue.BottomRight;
        float nBL = progress * deltaBL + oldValue.BottomLeft;

        return new CornerRadius(nTL, nTR, nBR, nBL);
    }
}
