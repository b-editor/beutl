namespace BEditorNext.Animation.Animators;

public sealed class UInt64Animator : Animator<ulong>
{
    public override ulong Interpolate(float progress, ulong oldValue, ulong newValue)
    {
        const float maxVal = ulong.MaxValue;

        var normOV = oldValue / maxVal;
        var normNV = newValue / maxVal;
        var deltaV = normNV - normOV;
        return (ulong)MathF.Round(maxVal * ((deltaV * progress) + normOV));
    }
}
