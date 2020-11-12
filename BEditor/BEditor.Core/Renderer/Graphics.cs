using System;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
#if OldOpenTK
using GLColor = OpenTK.Graphics.Color4;
#else
using GLColor = OpenTK.Mathematics.Color4;
using OpenTK.Mathematics;
#endif

using BEditor.Core.Media;
using Color = BEditor.Core.Media.Color;
using Image = BEditor.Core.Media.Image;
using Size = BEditor.Core.Media.Size;

namespace BEditor.Core.Renderer {
    public static class Graphics {

        #region GetPixels

        public static void GetPixels(Image image) {
            if (image == null) throw new ArgumentNullException("image");
            image.ThrowIfDisposed();

            GL.ReadBuffer(ReadBufferMode.Front);

            GL.ReadPixels(0, 0, image.Width, image.Height, image.Type, image.Type, image.Data);

            image.Flip(FlipMode.X);
        }

        public static void GetPixels(int width, int height, ImageType type, IntPtr ptr) {
            if (ptr == IntPtr.Zero) throw new Exception();

            GL.ReadBuffer(ReadBufferMode.Front);
            GL.ReadPixels(0, 0, width, height, type, type, ptr);
        }

        #endregion

        #region BindTexture

        public static void BindTexture(Image img, out int id) {
            if (img is null) {
                throw new ArgumentNullException(nameof(img));
            }
            img.ThrowIfDisposed();

            int width = img.Width;
            int height = img.Height;
            var type = img.Type;

            GL.GenTextures(1, out id);
            GL.BindTexture(TextureTarget.Texture2D, id);

            GL.TexImage2D(TextureTarget.Texture2D, 0, type, width, height, 0,
                type, type, img.Data);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);//必要
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);//必要
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);


            //GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        }

        #endregion

        #region Paint

        public static void Paint(Point3 coordinate, double nx, double ny, double nz, Point3 center, Action draw, Action blentfunc = null) {
            GL.Enable(EnableCap.Blend);

            blentfunc?.Invoke();
            if (blentfunc == null) {
                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            }

            GL.PushMatrix();
            {
                GL.Translate((Vector3)coordinate);

                GL.PushMatrix();
                {
                    GL.Rotate(nx, Vector3d.UnitX);
                    GL.Rotate(ny, Vector3d.UnitY);
                    GL.Rotate(nz, Vector3d.UnitZ);


                    GL.PushMatrix();
                    {
                        GL.Translate((Vector3)center);

                        draw?.Invoke();
                    }
                    GL.PopMatrix();
                }
                GL.PopMatrix();
            }
            GL.PopMatrix();
        }

        public static void DrawImage(
            Image img,
            float scalex = 0,
            float scaley = 0,
            float scalez = 0,
            Color? color = null,
            Color? ambient = null,
            Color? diffuse = null,
            Color? specular = null,
            float shininess = 10) {
            if (img is null) {
                throw new ArgumentNullException(nameof(img));
            }
            img.ThrowIfDisposed();

            BindTexture(img, out int id);

            GL.Color4((GLColor)(color ?? Color.White));
            GL.Material(MaterialFace.Front, MaterialParameter.Ambient, (GLColor)(ambient ?? Color.White));
            GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, (GLColor)(diffuse ?? Color.White));
            GL.Material(MaterialFace.Front, MaterialParameter.Specular, (GLColor)(specular ?? Color.White));
            GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shininess);

            GL.Enable(EnableCap.Texture2D);

            var w = img.Width / 2;
            var h = img.Height / 2;

            GL.BindTexture(TextureTarget.Texture2D, id);

            GL.Scale(scalex, scaley, scalez);
            GL.Begin(PrimitiveType.Quads);
            {
                GL.TexCoord2(1, 1);//右下
                GL.Vertex3(w, -h, 0);//右下

                GL.TexCoord2(0, 1);//左下
                GL.Vertex3(-w, -h, 0);//左下

                GL.TexCoord2(0, 0);//左上
                GL.Vertex3(-w, h, 0);//左上

                GL.TexCoord2(1, 0);//右上
                GL.Vertex3(w, h, 0);//右上
            }
            GL.End();

            GL.Disable(EnableCap.Texture2D);
            GL.Disable(EnableCap.Blend);

            GL.DeleteTexture(id);
        }

        #endregion

        #region LookAt

        public static void LookAt(float width, float height, float x, float y, float z, float tx, float ty, float tz, float near, float far, float fov, bool perspective) {
            if (perspective) {
                // 視体積の設定
                GL.MatrixMode(MatrixMode.Projection);
                Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(fov), (width / height), near, far, out var proj);//描画範囲

                GL.LoadMatrix(ref proj);

                GL.MatrixMode(MatrixMode.Modelview);

                // 視界の設定
                Matrix4 look = Matrix4.LookAt(new Vector3(x, y, z), new Vector3(tx, ty, tz), Vector3.UnitY);
                GL.LoadMatrix(ref look);

                GL.Enable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);
            }
            else {
                GL.MatrixMode(MatrixMode.Projection);
                // 視体積の設定
                Matrix4 proj = Matrix4.CreateOrthographic(width, height, near, far);
                GL.LoadMatrix(ref proj);

                GL.MatrixMode(MatrixMode.Modelview);

                // 視界の設定
                Matrix4 look = Matrix4.LookAt(new Vector3(x, y, z), new Vector3(tx, ty, tz), Vector3.UnitY);
                GL.LoadMatrix(ref look);


                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);
            }
        }

        public static void LookAt(Size size, Point3 point, Point3 target, float near, float far, float fov, bool perspective) {
            if (perspective) {
                // 視体積の設定
                GL.MatrixMode(MatrixMode.Projection);
                Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(fov), size.Aspect, near, far);//描画範囲

                GL.LoadMatrix(ref proj);

                GL.MatrixMode(MatrixMode.Modelview);

                // 視界の設定
                Matrix4 look = Matrix4.LookAt((Vector3)point, (Vector3)target, Vector3.UnitY);
                GL.LoadMatrix(ref look);


                GL.Enable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);
            }
            else {
                GL.MatrixMode(MatrixMode.Projection);
                // 視体積の設定
                Matrix4 proj = Matrix4.CreateOrthographic(size.Width, size.Height, near, far);
                GL.LoadMatrix(ref proj);

                GL.MatrixMode(MatrixMode.Modelview);

                // 視界の設定
                Matrix4 look = Matrix4.LookAt((Vector3)point, (Vector3)target, Vector3.UnitY);
                GL.LoadMatrix(ref look);


                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);
            }
        }

        #endregion

        #region DrawCube

        public static void DrawCube(float width, float height, float weight, Media.Color ambient, Media.Color diffuse, Media.Color specular, float shininess) {

            GL.Material(MaterialFace.Front, MaterialParameter.Ambient, (GLColor)ambient);
            GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, (GLColor)diffuse);
            GL.Material(MaterialFace.Front, MaterialParameter.Specular, (GLColor)specular);
            GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shininess);

            // 右面
            GL.Begin(PrimitiveType.TriangleStrip);
            {
                GL.Normal3(Vector3.UnitX);

                GL.Vertex3(width, height, -weight);
                GL.Vertex3(width, height, weight);
                GL.Vertex3(width, -height, -weight);
                GL.Vertex3(width, -height, weight);
            }
            GL.End();

            // 左面
            GL.Begin(PrimitiveType.TriangleStrip);
            {
                GL.Normal3(-Vector3.UnitX);

                GL.Vertex3(-width, -height, -weight);
                GL.Vertex3(-width, -height, weight);
                GL.Vertex3(-width, height, -weight);
                GL.Vertex3(-width, height, weight);
            }
            GL.End();

            // 上面
            GL.Begin(PrimitiveType.TriangleStrip);
            {
                GL.Normal3(Vector3.UnitY);

                GL.Vertex3(width, height, -weight);
                GL.Vertex3(-width, height, -weight);
                GL.Vertex3(width, height, weight);
                GL.Vertex3(-width, height, weight);
            }
            GL.End();

            // 下面
            GL.Begin(PrimitiveType.TriangleStrip);
            {
                GL.Normal3(-Vector3.UnitY);

                GL.Vertex3(-width, -height, weight);
                GL.Vertex3(-width, -height, -weight);
                GL.Vertex3(width, -height, weight);
                GL.Vertex3(width, -height, -weight);
            }
            GL.End();

            // 手前の面
            GL.Begin(PrimitiveType.TriangleStrip);
            {
                GL.Normal3(Vector3.UnitZ);

                GL.Vertex3(width, height, weight);
                GL.Vertex3(-width, height, weight);
                GL.Vertex3(width, -height, weight);
                GL.Vertex3(-width, -height, weight);
            }
            GL.End();

            // 奥の面
            GL.Begin(PrimitiveType.TriangleStrip);
            {
                GL.Normal3(-Vector3.UnitZ);

                GL.Vertex3(width, -height, -weight);
                GL.Vertex3(-width, -height, -weight);
                GL.Vertex3(width, height, -weight);
                GL.Vertex3(-width, height, -weight);
            }
            GL.End();
        }

        #endregion

        #region DrawBall

        public static void DrawBall(float radius, Media.Color ambient, Media.Color diffuse, Media.Color specular, float shininess, int count = 8) {
            GL.Material(MaterialFace.Front, MaterialParameter.Ambient, (GLColor)ambient);
            GL.Material(MaterialFace.Front, MaterialParameter.Diffuse, (GLColor)diffuse);
            GL.Material(MaterialFace.Front, MaterialParameter.Specular, (GLColor)specular);
            GL.Material(MaterialFace.Front, MaterialParameter.Shininess, shininess);

            float a = (float)(Math.PI / count / 2);
            float b = (float)(Math.PI / count / 2);

            GL.Begin(PrimitiveType.TriangleStrip);
            {
                for (int k = -count + 1; k <= count; k++) {
                    for (int i = 0; i <= count * 4; i++) {
                        Vector3 vec1 = new Vector3(radius * MathF.Cos(b * k) * MathF.Cos(a * i), radius * MathF.Cos(b * k) * MathF.Sin(a * i), radius * MathF.Sin(b * k));
                        GL.Normal3(vec1);
                        GL.Vertex3(vec1);

                        Vector3 vec2 = new Vector3(radius * MathF.Cos(b * (k - 1)) * MathF.Cos(a * i), radius * MathF.Cos(b * (k - 1)) * MathF.Sin(a * i), radius * MathF.Sin(b * (k - 1)));
                        GL.Normal3(vec2);
                        GL.Vertex3(vec2);
                    }
                }
            }
            GL.End();
        }

        #endregion
    }
}
