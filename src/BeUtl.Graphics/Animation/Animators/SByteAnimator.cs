namespace BeUtl.Animation.Animators;

public sealed class SByteAnimator : Animator<sbyte>
{
    public override sbyte Interpolate(float progress, sbyte oldValue, sbyte newValue)
    {
        const float maxVal = sbyte.MaxValue;

        var normOV = oldValue / maxVal;
        var normNV = newValue / maxVal;
        var deltaV = normNV - normOV;
        return (sbyte)MathF.Round(maxVal * ((deltaV * progress) + normOV));
    }
}
