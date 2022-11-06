namespace Beutl.Animation.Animators;

public sealed class UInt16Animator : Animator<ushort>
{
    public override ushort Interpolate(float progress, ushort oldValue, ushort newValue)
    {
        const float maxVal = ushort.MaxValue;

        var normOV = oldValue / maxVal;
        var normNV = newValue / maxVal;
        var deltaV = normNV - normOV;
        return (ushort)MathF.Round(maxVal * ((deltaV * progress) + normOV));
    }
}
