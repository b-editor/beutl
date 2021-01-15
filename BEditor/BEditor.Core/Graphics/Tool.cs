using System;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
using OpenTK.Mathematics;
#endif

using Color = BEditor.Drawing.Color;
using BEditor.Drawing;
using System.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Graphics
{
    internal static class Tool
    {
        #region DrawBall

        public static void DrawBall(float radius, Color ambient, Color diffuse, Color specular, float shininess, int count = 8)
        {
            //GL.Material(MaterialFace.Front, MaterialParameter.Ambient, ambient.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, diffuse.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Specular, specular.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shininess);

            //float a = (float)(Math.PI / count / 2);
            //float b = (float)(Math.PI / count / 2);

            //GL.Begin(PrimitiveType.TriangleStrip);
            //{
            //    for (int k = -count + 1; k <= count; k++)
            //    {
            //        for (int i = 0; i <= count * 4; i++)
            //        {
            //            Vector3 vec1 = new Vector3(radius * MathF.Cos(b * k) * MathF.Cos(a * i), radius * MathF.Cos(b * k) * MathF.Sin(a * i), radius * MathF.Sin(b * k));
            //            GL.Normal3(vec1);
            //            GL.Vertex3(vec1);

            //            Vector3 vec2 = new Vector3(radius * MathF.Cos(b * (k - 1)) * MathF.Cos(a * i), radius * MathF.Cos(b * (k - 1)) * MathF.Sin(a * i), radius * MathF.Sin(b * (k - 1)));
            //            GL.Normal3(vec2);
            //            GL.Vertex3(vec2);
            //        }
            //    }
            //}
            //GL.End();
        }

        #endregion

        internal static Vector3 ToOpenTK(this ref System.Numerics.Vector3 vector3) => new Vector3(vector3.X, vector3.Y, vector3.Z);
        internal static Vector2 ToOpenTK(this ref System.Numerics.Vector2 vector3) => new Vector2(vector3.X, vector3.Y);
        internal static Vector4 ToVector4(this in Color color)
            => new(
                color.R / 255f,
                color.G / 255f,
                color.B / 255f,
                color.A / 255f);
        internal static System.Numerics.Vector3 ToNumerics(this ref Vector3 vector3) => new(vector3.X, vector3.Y, vector3.Z);
        internal static System.Numerics.Vector2 ToNumerics(this ref Vector2 vector3) => new(vector3.X, vector3.Y);
        internal static GLColor ToOpenTK(this in Color color) => new(color.R, color.G, color.B, color.A);
    }
}
