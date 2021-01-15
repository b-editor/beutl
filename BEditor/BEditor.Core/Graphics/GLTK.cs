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
    public static class GLTK
    {
        #region Paint

        public static void Paint<T>(System.Numerics.Vector3 coordinate, double nx, double ny, double nz, System.Numerics.Vector3 center, T state, Action<T> draw, Action blentfunc = null)
        {
            //GL.Enable(EnableCap.Blend);

            //blentfunc?.Invoke();
            //if (blentfunc == null)
            //{
            //    GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            //    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            //}

            //GL.PushMatrix();
            //{
            //    GL.Translate(coordinate.ToOpenTK());

            //    GL.PushMatrix();
            //    {
            //        GL.Rotate(nx, Vector3d.UnitX);
            //        GL.Rotate(ny, Vector3d.UnitY);
            //        GL.Rotate(nz, Vector3d.UnitZ);


            //        GL.PushMatrix();
            //        {
            //            GL.Translate(center.ToOpenTK());

            //            draw?.Invoke(state);
            //        }
            //        GL.PopMatrix();
            //    }
            //    GL.PopMatrix();
            //}
            //GL.PopMatrix();
        }
        public static void Paint(System.Numerics.Vector3 coordinate, double nx, double ny, double nz, System.Numerics.Vector3 center, Action draw, Action blentfunc = null)
        {
            //GL.Enable(EnableCap.Blend);

            //blentfunc?.Invoke();
            //if (blentfunc == null)
            //{
            //    GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            //    GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            //}

            //GL.PushMatrix();
            //{
            //    GL.Translate(coordinate.ToOpenTK());

            //    GL.PushMatrix();
            //    {
            //        GL.Rotate(nx, Vector3d.UnitX);
            //        GL.Rotate(ny, Vector3d.UnitY);
            //        GL.Rotate(nz, Vector3d.UnitZ);


            //        GL.PushMatrix();
            //        {
            //            GL.Translate(center.ToOpenTK());

            //            draw?.Invoke();
            //        }
            //        GL.PopMatrix();
            //    }
            //    GL.PopMatrix();
            //}
            //GL.PopMatrix();
        }

        public static void DrawImage(
            Image<BGRA32> img,
            float scalex = 1,
            float scaley = 1,
            float scalez = 1,
            Color? color = null,
            Color? ambient = null,
            Color? diffuse = null,
            Color? specular = null,
            float shininess = 10)
        {
            //if (img is null)
            //{
            //    throw new ArgumentNullException(nameof(img));
            //}
            //img.ThrowIfDisposed();

            //BindTexture(img, out int id);

            //GL.Color4((color ?? Color.Light).ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Ambient, (ambient ?? Color.Light).ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, (diffuse ?? Color.Light).ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Specular, (specular ?? Color.Light).ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shininess);

            //GL.Enable(EnableCap.Texture2D);

            //var w = img.Width / 2;
            //var h = img.Height / 2;

            //GL.BindTexture(TextureTarget.Texture2D, id);

            //GL.Scale(scalex, scaley, scalez);
            //GL.Begin(PrimitiveType.Quads);
            //{
            //    GL.TexCoord2(1, 1);//右下
            //    GL.Vertex3(w, -h, 0);//右下

            //    GL.TexCoord2(0, 1);//左下
            //    GL.Vertex3(-w, -h, 0);//左下

            //    GL.TexCoord2(0, 0);//左上
            //    GL.Vertex3(-w, h, 0);//左上

            //    GL.TexCoord2(1, 0);//右上
            //    GL.Vertex3(w, h, 0);//右上
            //}
            //GL.End();

            //GL.Disable(EnableCap.Texture2D);
            //GL.Disable(EnableCap.Blend);

            //GL.DeleteTexture(id);
        }

        #endregion

        #region LookAt

        public static void LookAt(float width, float height, float x, float y, float z, float tx, float ty, float tz, float near, float far, float fov, bool perspective)
        {
            //if (perspective)
            //{
            //    // 視体積の設定
            //    GL.MatrixMode(MatrixMode.Projection);
            //    Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(fov), (width / height), near, far, out var proj);//描画範囲

            //    GL.LoadMatrix(ref proj);

            //    GL.MatrixMode(MatrixMode.Modelview);

            //    // 視界の設定
            //    Matrix4 look = Matrix4.LookAt(new Vector3(x, y, z), new Vector3(tx, ty, tz), Vector3.UnitY);
            //    GL.LoadMatrix(ref look);

            //    GL.Enable(EnableCap.DepthTest);
            //    GL.Disable(EnableCap.Lighting);
            //}
            //else
            //{
            //    GL.MatrixMode(MatrixMode.Projection);
            //    // 視体積の設定
            //    Matrix4 proj = Matrix4.CreateOrthographic(width, height, near, far);
            //    GL.LoadMatrix(ref proj);

            //    GL.MatrixMode(MatrixMode.Modelview);

            //    // 視界の設定
            //    Matrix4 look = Matrix4.LookAt(new Vector3(x, y, z), new Vector3(tx, ty, tz), Vector3.UnitY);
            //    GL.LoadMatrix(ref look);


            //    GL.Disable(EnableCap.DepthTest);
            //    GL.Disable(EnableCap.Lighting);
            //}
        }

        #endregion

        #region DrawCube

        public static void DrawCube(float width, float height, float weight, Color ambient, Color diffuse, Color specular, float shininess)
        {

            //GL.Material(MaterialFace.Front, MaterialParameter.Ambient, ambient.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, diffuse.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Specular, specular.ToOpenTK());
            //GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shininess);

            //// 右面
            //GL.Begin(PrimitiveType.TriangleStrip);
            //{
            //    GL.Normal3(Vector3.UnitX);

            //    GL.Vertex3(width, height, -weight);
            //    GL.Vertex3(width, height, weight);
            //    GL.Vertex3(width, -height, -weight);
            //    GL.Vertex3(width, -height, weight);
            //}
            //GL.End();

            //// 左面
            //GL.Begin(PrimitiveType.TriangleStrip);
            //{
            //    GL.Normal3(-Vector3.UnitX);

            //    GL.Vertex3(-width, -height, -weight);
            //    GL.Vertex3(-width, -height, weight);
            //    GL.Vertex3(-width, height, -weight);
            //    GL.Vertex3(-width, height, weight);
            //}
            //GL.End();

            //// 上面
            //GL.Begin(PrimitiveType.TriangleStrip);
            //{
            //    GL.Normal3(Vector3.UnitY);

            //    GL.Vertex3(width, height, -weight);
            //    GL.Vertex3(-width, height, -weight);
            //    GL.Vertex3(width, height, weight);
            //    GL.Vertex3(-width, height, weight);
            //}
            //GL.End();

            //// 下面
            //GL.Begin(PrimitiveType.TriangleStrip);
            //{
            //    GL.Normal3(-Vector3.UnitY);

            //    GL.Vertex3(-width, -height, weight);
            //    GL.Vertex3(-width, -height, -weight);
            //    GL.Vertex3(width, -height, weight);
            //    GL.Vertex3(width, -height, -weight);
            //}
            //GL.End();

            //// 手前の面
            //GL.Begin(PrimitiveType.TriangleStrip);
            //{
            //    GL.Normal3(Vector3.UnitZ);

            //    GL.Vertex3(width, height, weight);
            //    GL.Vertex3(-width, height, weight);
            //    GL.Vertex3(width, -height, weight);
            //    GL.Vertex3(-width, -height, weight);
            //}
            //GL.End();

            //// 奥の面
            //GL.Begin(PrimitiveType.TriangleStrip);
            //{
            //    GL.Normal3(-Vector3.UnitZ);

            //    GL.Vertex3(width, -height, -weight);
            //    GL.Vertex3(-width, -height, -weight);
            //    GL.Vertex3(width, height, -weight);
            //    GL.Vertex3(-width, height, -weight);
            //}
            //GL.End();
        }

        #endregion

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
