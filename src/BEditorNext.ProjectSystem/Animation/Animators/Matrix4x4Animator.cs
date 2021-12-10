using System.Numerics;

namespace BEditorNext.Animation.Animators;

public sealed class Matrix4x4Animator : Animator<Matrix4x4>
{
    public override Matrix4x4 Multiply(Matrix4x4 left, float right)
    {
        return left * right;
    }
}
