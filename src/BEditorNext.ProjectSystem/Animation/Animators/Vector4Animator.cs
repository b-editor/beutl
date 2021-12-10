using System.Numerics;

namespace BEditorNext.Animation.Animators;

public sealed class Vector4Animator : Animator<Vector4>
{
    public override Vector4 Multiply(Vector4 left, float right)
    {
        return left * right;
    }
}
