using System.Numerics;

namespace Beutl.Animation.Animators;

public sealed class Matrix3x2Animator : Animator<Matrix3x2>
{
    public override Matrix3x2 Interpolate(float progress, Matrix3x2 oldValue, Matrix3x2 newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
