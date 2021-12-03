namespace BEditorNext.Animation.Animators;

public sealed class Int16Animator : Animator<short>
{
    public override short Interpolate(float progress, short oldValue, short newValue)
    {
        const float maxVal = short.MaxValue;

        var normOV = oldValue / maxVal;
        var normNV = newValue / maxVal;
        var deltaV = normNV - normOV;
        return (short)MathF.Round(maxVal * ((deltaV * progress) + normOV));
    }
}
