using BEditorNext.Graphics;

namespace BEditorNext.Animation.Animators;

public sealed class VectorAnimator : Animator<Vector>
{
    public override Vector Interpolate(float progress, Vector oldValue, Vector newValue)
    {
        return ((newValue - oldValue) * progress) + oldValue;
    }
}
