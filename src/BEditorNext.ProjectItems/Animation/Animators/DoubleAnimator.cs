namespace BEditorNext.Animation.Animators;

public sealed class DoubleAnimator : Animator<double>
{
    public override double Interpolate(float progress, double oldValue, double newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
