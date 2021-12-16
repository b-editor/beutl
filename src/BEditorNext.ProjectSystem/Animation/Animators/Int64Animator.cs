namespace BEditorNext.Animation.Animators;

public sealed class Int64Animator : Animator<long>
{
    public override long Interpolate(float progress, long oldValue, long newValue)
    {
        const float maxVal = long.MaxValue;

        var normOV = oldValue / maxVal;
        var normNV = newValue / maxVal;
        var deltaV = normNV - normOV;
        return (long)MathF.Round(maxVal * ((deltaV * progress) + normOV));
    }
}
