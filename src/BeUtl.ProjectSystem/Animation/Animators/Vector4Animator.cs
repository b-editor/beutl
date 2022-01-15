using System.Numerics;

namespace BeUtl.Animation.Animators;

public sealed class Vector4Animator : Animator<Vector4>
{
    public override Vector4 Interpolate(float progress, Vector4 oldValue, Vector4 newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
