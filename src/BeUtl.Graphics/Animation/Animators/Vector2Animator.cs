using System.Numerics;

namespace Beutl.Animation.Animators;

public sealed class Vector2Animator : Animator<Vector2>
{
    public override Vector2 Interpolate(float progress, Vector2 oldValue, Vector2 newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
