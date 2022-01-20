namespace BeUtl.Animation.Animators;

public sealed class ByteAnimator : Animator<byte>
{
    public override byte Interpolate(float progress, byte oldValue, byte newValue)
    {
        const float maxVal = byte.MaxValue;

        var normOV = oldValue / maxVal;
        var normNV = newValue / maxVal;
        var deltaV = normNV - normOV;
        return (byte)MathF.Round(maxVal * ((deltaV * progress) + normOV));
    }
}
