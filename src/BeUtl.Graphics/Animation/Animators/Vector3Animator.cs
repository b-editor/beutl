using System.Numerics;

namespace BeUtl.Animation.Animators;

public sealed class Vector3Animator : Animator<Vector3>
{
    public override Vector3 Interpolate(float progress, Vector3 oldValue, Vector3 newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
