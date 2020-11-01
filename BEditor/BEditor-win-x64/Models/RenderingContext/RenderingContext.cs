using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;

namespace BEditor.Models {
    public class RenderingContext : BEditor.Core.Renderer.BaseRenderingContext {
        public override int Width { get => GLControl.Width; protected set => GLControl.Width = value; }
        public override int Height { get => GLControl.Height; protected set => GLControl.Height = value; }

        private GLControl GLControl = new GLControl();

        public RenderingContext(int width, int height) : base(width, height) {
            Initialize();
        }

        public override void MakeCurrent() => GLControl.MakeCurrent();
        public override void SwapBuffers() => GLControl.SwapBuffers();

        public override void Clear(int width, int height, bool Perspective = false, float x = 0, float y = 0, float z = 1024, float tx = 0, float ty = 0, float tz = 0, float near = 0.1F, float far = 20000) {
            Width = width;
            Height = height;

            MakeCurrent();

            Dispose();

            #region FBO

            //色
            GL.GenTextures(1, out ColorTex);
            GL.BindTexture(TextureTarget.Texture2D, ColorTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, Width, Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            //深度
            GL.GenTextures(1, out DepthTex);
            GL.BindTexture(TextureTarget.Texture2D, DepthTex);
            GL.TexImage2D(TextureTarget.Texture2D, 0, (PixelInternalFormat)All.DepthComponent32, Width, Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.DepthComponent, PixelType.UnsignedInt, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            //FBO
            GL.Ext.GenFramebuffers(1, out FBO);
            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, FBO);
            GL.Ext.FramebufferTexture2D(FramebufferTarget.FramebufferExt, FramebufferAttachment.ColorAttachment0Ext, TextureTarget.Texture2D, ColorTex, 0);
            GL.Ext.FramebufferTexture2D(FramebufferTarget.FramebufferExt, FramebufferAttachment.DepthAttachmentExt, TextureTarget.Texture2D, DepthTex, 0);
            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, 0);


            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, FBO);

            #endregion

            //ビューポートの設定
            GL.Viewport(0, 0, Width, Height);

            if (Perspective) {
                //視体積の設定
                GL.MatrixMode(MatrixMode.Projection);
                Matrix4 proj = Matrix4.CreatePerspectiveFieldOfView(MathHelper.PiOver4, Aspect, near, far);//描画範囲

                GL.LoadMatrix(ref proj);
            }
            else {
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


            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            if (Perspective) {
                GL.Enable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);
            }
            else {
                GL.Disable(EnableCap.DepthTest);
                GL.Disable(EnableCap.Lighting);

            }
        }

        ~RenderingContext() {
            GLControl.Dispose();
        }
    }
}
