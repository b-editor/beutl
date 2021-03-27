using OpenTK.Mathematics;

using Color = BEditor.Drawing.Color;
using Matrix4x4 = System.Numerics.Matrix4x4;

namespace BEditor.Audio
{
    static class Tool
    {
        internal static Vector3 ToOpenTK(this in System.Numerics.Vector3 vector3)
        {
            return new Vector3(vector3.X, vector3.Y, vector3.Z);
        }

        internal static Vector4 ToOpenTK(this in System.Numerics.Vector4 vector4)
        {
            return new Vector4(vector4.X, vector4.Y, vector4.Z, vector4.W);
        }

        internal static Matrix4 ToOpenTK(this in Matrix4x4 mat)
        {
            return new Matrix4(
                mat.M11, mat.M12, mat.M13, mat.M14,
                mat.M21, mat.M22, mat.M23, mat.M24,
                mat.M31, mat.M32, mat.M33, mat.M34,
                mat.M41, mat.M42, mat.M43, mat.M44);
        }

        internal static Vector2 ToOpenTK(this in System.Numerics.Vector2 vector3)
        {
            return new Vector2(vector3.X, vector3.Y);
        }

        internal static System.Numerics.Vector3 ToVector3(this in Color color)
        {
            return new(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f);
        }

        internal static System.Numerics.Vector4 ToVector4(this in Color color)
        {
            return new(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f);
        }

        internal static System.Numerics.Vector3 ToNumerics(this in Vector3 vector3)
        {
            return new(vector3.X, vector3.Y, vector3.Z);
        }

        internal static Matrix4x4 ToNumerics(this in Matrix4 mat)
        {
            return new(
                mat.M11, mat.M12, mat.M13, mat.M14,
                mat.M21, mat.M22, mat.M23, mat.M24,
                mat.M31, mat.M32, mat.M33, mat.M34,
                mat.M41, mat.M42, mat.M43, mat.M44);
        }

        internal static System.Numerics.Vector2 ToNumerics(this in Vector2 vector3)
        {
            return new(vector3.X, vector3.Y);
        }
    }
}
