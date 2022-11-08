namespace Beutl.Animation.Animators;

public sealed class Int32Animator : Animator<int>
{
    public override int Interpolate(float progress, int oldValue, int newValue)
    {
        const float maxVal = int.MaxValue;

        var normOV = oldValue / maxVal;
        var normNV = newValue / maxVal;
        var deltaV = normNV - normOV;
        return (int)MathF.Round(maxVal * ((deltaV * progress) + normOV));
    }
}
