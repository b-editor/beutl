using System.Numerics;

namespace BEditorNext.Animation.Animators;

public sealed class Matrix3x2Animator : Animator<Matrix3x2>
{
    public override Matrix3x2 Multiply(Matrix3x2 left, float right)
    {
        return left * right;
    }
}
