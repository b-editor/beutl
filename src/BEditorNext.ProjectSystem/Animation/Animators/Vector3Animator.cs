using System.Numerics;

namespace BEditorNext.Animation.Animators;

public sealed class Vector3Animator : Animator<Vector3>
{
    public override Vector3 Multiply(Vector3 left, float right)
    {
        return left * right;
    }
}
