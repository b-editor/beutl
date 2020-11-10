using System;

using OpenTK.Graphics.GL;
using OpenTK.Graphics.OpenGL;
using OpenTK.Graphics;
using OpenTK.Mathematics;
using OpenCvSharp;
using OpenTK.Windowing.Desktop;

namespace OpenTKTest {
    class Program {
        const int width = 500;
        const int height = 500;

        static int fbID, rbID, texID;
        const int FB_WIDTH = 500, FB_HEIGHT = 500;

        static void Main(string[] args) {
            using (new GameWindow(GameWindowSettings.Default, NativeWindowSettings.Default)) ;

            // renderbuffer object の生成
            GL.Ext.GenRenderbuffers(1, out rbID);
            GL.Ext.BindRenderbuffer(RenderbufferTarget.RenderbufferExt, rbID);

            // 第二引数は 色 GL_RGB, GL_RGBA, デプス値 GL_DEPTH_COMPONENT, ステンシル値 GL_STENCIL_INDEX　などを指定できる
            GL.Ext.RenderbufferStorage(RenderbufferTarget.RenderbufferExt, RenderbufferStorage.Rgba8, FB_WIDTH, FB_HEIGHT);

            // framebuffer object の生成
            GL.Ext.GenFramebuffers(1, out fbID);
            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, fbID);

            // renderbufferをframebuffer objectに結びつける
            // 第二引数は GL_COLOR_ATTACHMENTn_EXT, GL_DEPTH_ATTACHMENT_EXT, GL_STENCIL_ATTACHMENT_EXT など
            GL.Ext.FramebufferRenderbuffer(FramebufferTarget.FramebufferExt, FramebufferAttachment.ColorAttachment0Ext, RenderbufferTarget.RenderbufferExt, rbID);

            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, 0);

            GL.Ext.FramebufferTexture2D(FramebufferTarget.FramebufferExt, FramebufferAttachment.ColorAttachment0Ext, TextureTarget.Texture2D, texID, 0);
            GL.Ext.FramebufferRenderbuffer(FramebufferTarget.FramebufferExt, FramebufferAttachment.DepthAttachmentExt, RenderbufferTarget.RenderbufferExt, rbID);

            //ビューポートの設定
            GL.Viewport(0, 0, width, height);

            GL.MatrixMode(MatrixMode.Projection);
            //視体積の設定
            var proj = Matrix4.CreateOrthographic(width, height, 0.1F, 20000);
            GL.LoadMatrix(ref proj);

            GL.MatrixMode(MatrixMode.Modelview);

            //視界の設定
            var look = Matrix4.LookAt(new Vector3(0, 0, 1024), new Vector3(0, 0, 0), Vector3.UnitY);
            GL.LoadMatrix(ref look);


            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.Lighting);

            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, fbID);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.Color4(255, 255, 255, 255);
            GL.Begin(PrimitiveType.Quads);
            {
                GL.Vertex2(250, -250);

                GL.Vertex2(-250, -250);

                GL.Vertex2(-250, 250);

                GL.Vertex2(250, 250);
            }
            GL.End();

            //glReadBuffer(GL_COLOR_ATTACHMENT0_EXT);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
            var mat = new Mat(500, 500, MatType.CV_8UC4);
            GL.ReadPixels(0, 0, 500, 500, PixelFormat.Bgra, PixelType.UnsignedByte, mat.Data);

            mat.Flip(FlipMode.Y);

            mat.SaveImage("Test.png");
            mat.Dispose();
            //glBindFramebufferEXT(GL_FRAMEBUFFER_EXT, 0);
            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, 0);
        }
    }
}
