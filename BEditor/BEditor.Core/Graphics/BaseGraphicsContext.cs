using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

#if !OldOpenTK
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
#else

#endif
using Color = BEditor.Drawing.Color;
using System.Runtime.InteropServices;
using BEditor.Core.Renderings;
using BEditor.Core.Data.Primitive.Objects;
using BEditor.Core.Data.Primitive.Properties.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Graphics
{
    public abstract class BaseGraphicsContext : IDisposable
    {
        protected int Color;
        protected int Depth;
        protected int FBO;

        public BaseGraphicsContext(int width, int height)
        {
            Width = width;
            Height = height;
        }


        public virtual int Width { get; private set; }
        public virtual int Height { get; private set; }
        public float Aspect => ((float)Width) / ((float)Height);
        public bool IsInitialized { get; private set; }


        public abstract void MakeCurrent();
        public abstract void SwapBuffers();
        public virtual void Dispose()
        {
            if (IsInitialized)
            {
                GL.DeleteTexture(Color);
                GL.DeleteTexture(Depth);
                GL.Ext.DeleteFramebuffer(FBO);
            }
        }
        /// <summary>
        /// カメラを設定
        /// </summary>
        /// <param name="Perspective">遠近法を使うか</param>
        /// <param name="x">CameraX</param>
        /// <param name="y">CameraY</param>
        /// <param name="z">CameraZ</param>
        /// <param name="tx">TargetX</param>
        /// <param name="ty">TargetY</param>
        /// <param name="tz">TargetZ</param>
        /// <param name="near">ZNear</param>
        /// <param name="far">ZFar</param>
        public virtual void Clear(
            bool Perspective = false,
            float x = 0, float y = 0, float z = 1024,
            float tx = 0, float ty = 0, float tz = 0,
            float near = 0.1F, float far = 20000)
        {
            MakeCurrent();

            if (Perspective)
            {
                //視体積の設定
                GL.MatrixMode(MatrixMode.Projection);
                Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.PiOver4, Aspect, near, far);//描画範囲

                GL.LoadMatrix(ref proj);
            }
            else
            {
                GL.MatrixMode(MatrixMode.Projection);
                //視体積の設定
                Matrix4 proj = Matrix4.CreateOrthographic(Width, Height, near, far);
                GL.LoadMatrix(ref proj);
            }

            GL.MatrixMode(MatrixMode.Modelview);

            //視界の設定
            Matrix4 look = Matrix4.LookAt(new Vector3(x, y, z), new Vector3(tx, ty, tz), Vector3.UnitY);
            GL.LoadMatrix(ref look);


            //法線の自動調節
            GL.Enable(EnableCap.Normalize);
            //アンチエイリアス
            GL.Enable(EnableCap.LineSmooth);
            GL.Enable(EnableCap.PolygonSmooth);
            GL.Enable(EnableCap.PointSmooth);

            GL.Hint(HintTarget.FogHint, HintMode.Nicest);
            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
            GL.Hint(HintTarget.PointSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);

            if (Perspective)
            {
                GL.Enable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);
            }
            else
            {
                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);
            }

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        public virtual void Resize(
            int width, int height, bool Perspective = false,
            float x = 0, float y = 0, float z = 1024,
            float tx = 0, float ty = 0, float tz = 0,
            float near = 0.1F, float far = 20000)
        {
            MakeCurrent();

            Width = width;
            Height = height;

            if (IsInitialized)
            {
                GL.DeleteTexture(Color);
                GL.DeleteTexture(Depth);
                GL.Ext.DeleteFramebuffer(FBO);
            }

            #region FBO

            //色
            GL.GenTextures(1, out Color);
            GL.BindTexture(TextureTarget.Texture2D, Color);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, Width, Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            //深度
            GL.GenTextures(1, out Depth);
            GL.BindTexture(TextureTarget.Texture2D, Depth);
            GL.TexImage2D(TextureTarget.Texture2D, 0, (PixelInternalFormat)All.DepthComponent32, Width, Height, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            //FBO
            GL.Ext.GenFramebuffers(1, out FBO);
            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, FBO);
            GL.Ext.FramebufferTexture2D(FramebufferTarget.FramebufferExt, FramebufferAttachment.ColorAttachment0Ext, TextureTarget.Texture2D, Color, 0);
            GL.Ext.FramebufferTexture2D(FramebufferTarget.FramebufferExt, FramebufferAttachment.DepthAttachmentExt, TextureTarget.Texture2D, Depth, 0);
            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, 0);

            #endregion

            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, FBO);

            //ビューポートの設定
            GL.Viewport(0, 0, width, height);

            if (Perspective)
            {
                //視体積の設定
                GL.MatrixMode(MatrixMode.Projection);
                Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.PiOver4, Aspect, near, far);//描画範囲

                GL.LoadMatrix(ref proj);
            }
            else
            {
                GL.MatrixMode(MatrixMode.Projection);
                //視体積の設定
                Matrix4 proj = Matrix4.CreateOrthographic(Width, Height, near, far);
                GL.LoadMatrix(ref proj);
            }

            GL.MatrixMode(MatrixMode.Modelview);

            //視界の設定
            Matrix4 look = Matrix4.LookAt(new Vector3(x, y, z), new Vector3(tx, ty, tz), Vector3.UnitY);
            GL.LoadMatrix(ref look);


            //法線の自動調節
            GL.Enable(EnableCap.Normalize);
            //アンチエイリアス
            GL.Enable(EnableCap.LineSmooth);
            GL.Enable(EnableCap.PolygonSmooth);
            GL.Enable(EnableCap.PointSmooth);

            GL.Hint(HintTarget.FogHint, HintMode.Nicest);
            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
            GL.Hint(HintTarget.PointSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);

            if (Perspective)
            {
                GL.Enable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);
            }
            else
            {
                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);
            }

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        protected void Initialize()
        {
            Resize(Width, Height);

            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Lighting);

            IsInitialized = true;
        }

        internal void DrawImage(Image<BGRA32> img, ClipData data, EffectRenderArgs args)
        {
            if (img == null) return;

            var frame = args.Frame;
            var drawObject = data.Effect[0] as ImageObject;

            float alpha = (float)(drawObject.Blend.Alpha.GetValue(frame) / 100);

            var scale = (float)(drawObject.Zoom.Scale.GetValue(frame) / 100);
            var scalex = (float)(drawObject.Zoom.ScaleX.GetValue(frame) / 100) * scale;
            var scaley = (float)(drawObject.Zoom.ScaleY.GetValue(frame) / 100) * scale;
            var scalez = (float)(drawObject.Zoom.ScaleZ.GetValue(frame) / 100) * scale;

            var coordinate = new System.Numerics.Vector3(
                drawObject.Coordinate.X.GetValue(frame),
                drawObject.Coordinate.Y.GetValue(frame),
                drawObject.Coordinate.Z.GetValue(frame));

            var center = new System.Numerics.Vector3(
                drawObject.Coordinate.CenterX.GetValue(frame),
                drawObject.Coordinate.CenterY.GetValue(frame),
                drawObject.Coordinate.CenterZ.GetValue(frame));


            var nx = drawObject.Angle.AngleX.GetValue(frame);
            var ny = drawObject.Angle.AngleY.GetValue(frame);
            var nz = drawObject.Angle.AngleZ.GetValue(frame);

            Color ambient = drawObject.Material.Ambient.GetValue(frame);
            Color diffuse = drawObject.Material.Diffuse.GetValue(frame);
            Color specular = drawObject.Material.Specular.GetValue(frame);
            float shininess = drawObject.Material.Shininess.GetValue(frame);
            var c = drawObject.Blend.Color.GetValue(frame);
            var color = Drawing.Color.FromARGB((byte)(c.A * alpha), c.R, c.G, c.B);

            MakeCurrent();

            GLTK.Paint(
                coordinate,
                nx, ny, nz, center,
                (img, scalex, scaley, scalez, color, ambient, diffuse, specular, shininess, args, data),
                s =>
                {
                    if (data.Parent.SelectItem == data && args.Type is RenderType.Preview)
                    {
                        var w = s.img.Width / 2 + 25;
                        var h = s.img.Height / 2 + 25;

                        GL.Color4(Color4.White);
                        GL.LineWidth(2);

                        #region 上
                        // 上
                        GL.Begin(PrimitiveType.Lines);
                        {
                            GL.Vertex2(w, h);
                            GL.Vertex2(-w, h);
                        }
                        GL.End();
                        #endregion

                        #region 下
                        // 下
                        GL.Begin(PrimitiveType.Lines);
                        {
                            GL.Vertex2(w, -h);
                            GL.Vertex2(-w, -h);
                        }
                        GL.End();
                        #endregion

                        #region 右
                        // 右
                        GL.Begin(PrimitiveType.Lines);
                        {
                            GL.Vertex2(w, -h);
                            GL.Vertex2(w, h);
                        }
                        GL.End();
                        #endregion

                        #region 左
                        // 左
                        GL.Begin(PrimitiveType.Lines);
                        {
                            GL.Vertex2(-w, -h);
                            GL.Vertex2(-w, h);
                        }
                        GL.End();
                        #endregion
                    }

                    GLTK.DrawImage(s.img, s.scalex, s.scaley, s.scalez, s.color, s.ambient, s.diffuse, s.specular, s.shininess);
                },
                Blend.BlentFunc[drawObject.Blend.BlendType.Index]);
        }
        public void ReadPixels(Image<BGRA32> image)
        {
            MakeCurrent();
            GLTK.GetPixels(image);
        }
    }
}
