using BEditorNext.Graphics;

namespace BEditorNext.Animation.Animators;

public sealed class MatrixAnimator : Animator<Matrix>
{
    public override Matrix Interpolate(float progress, Matrix oldValue, Matrix newValue)
    {
        var newM= ToMatrix3x2(newValue);
        var oldM= ToMatrix3x2(oldValue);

        return ToMatrix(((newM - oldM) * progress) + oldM);
    }

    private static Matrix ToMatrix(System.Numerics.Matrix3x2 m)
    {
        return new Matrix(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);
    }

    private static System.Numerics.Matrix3x2 ToMatrix3x2(Matrix m)
    {
        return new System.Numerics.Matrix3x2(m.M11, m.M12, m.M21, m.M22, m.M31, m.M32);
    }
}
