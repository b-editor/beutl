using System.Numerics;

namespace BeUtl.Animation.Animators;

public sealed class Matrix4x4Animator : Animator<Matrix4x4>
{
    public override Matrix4x4 Interpolate(float progress, Matrix4x4 oldValue, Matrix4x4 newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
