using System.Numerics;

namespace BEditorNext.Animation.Animators;

public sealed class Vector2Animator : Animator<Vector2>
{
    public override Vector2 Multiply(Vector2 left, float right)
    {
        return left * right;
    }
}
