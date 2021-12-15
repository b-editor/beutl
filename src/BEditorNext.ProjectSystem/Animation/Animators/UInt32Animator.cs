namespace BEditorNext.Animation.Animators;

public sealed class UInt32Animator : Animator<uint>
{
    public override uint Interpolate(float progress, uint oldValue, uint newValue)
    {
        const float maxVal = uint.MaxValue;

        var normOV = oldValue / maxVal;
        var normNV = newValue / maxVal;
        var deltaV = normNV - normOV;
        return (uint)MathF.Round(maxVal * ((deltaV * progress) + normOV));
    }
}
