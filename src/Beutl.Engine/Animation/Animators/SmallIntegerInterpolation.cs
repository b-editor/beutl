using System.Numerics;

namespace Beutl.Animation.Animators;

internal static class SmallIntegerInterpolation
{
    // Interpolates in the range normalized by T.MaxValue and returns the rounded value. Callers cast it
    // themselves: T.CreateTruncating saturates where the cast wraps, which an overshooting easing reaches.
    public static float Interpolate<T>(float progress, T oldValue, T newValue)
        where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        float maxVal = float.CreateTruncating(T.MaxValue);

        var normOV = float.CreateTruncating(oldValue) / maxVal;
        var normNV = float.CreateTruncating(newValue) / maxVal;
        var deltaV = normNV - normOV;
        return MathF.Round(maxVal * ((deltaV * progress) + normOV));
    }
}
