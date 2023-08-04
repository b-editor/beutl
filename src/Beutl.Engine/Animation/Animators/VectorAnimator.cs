using Beutl.Graphics;

namespace Beutl.Animation.Animators;

public sealed class VectorAnimator : Animator<Vector>
{
    public override Vector Interpolate(float progress, Vector oldValue, Vector newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
