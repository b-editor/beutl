using System;

using OpenTK.Graphics.GL;
using OpenTK.Graphics.OpenGL;
using OpenTK.Graphics;
using OpenTK.Mathematics;
using OpenCvSharp;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;

namespace OpenTKTest {
    class Program {
        const int width = 500;
        const int height = 500;

        static int fbID, rbID, texID;
        const int FB_WIDTH = 500, FB_HEIGHT = 500;

        static void Main(string[] args) {
            using (Game window = new Game()) {
                window.Run();
            }

            #region
            //// renderbuffer object の生成
            //GL.Ext.GenRenderbuffers(1, out rbID);
            //GL.Ext.BindRenderbuffer(RenderbufferTarget.RenderbufferExt, rbID);

            //// 第二引数は 色 GL_RGB, GL_RGBA, デプス値 GL_DEPTH_COMPONENT, ステンシル値 GL_STENCIL_INDEX　などを指定できる
            //GL.Ext.RenderbufferStorage(RenderbufferTarget.RenderbufferExt, RenderbufferStorage.Rgba8, FB_WIDTH, FB_HEIGHT);

            //// framebuffer object の生成
            //GL.Ext.GenFramebuffers(1, out fbID);
            //GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, fbID);

            //// renderbufferをframebuffer objectに結びつける
            //// 第二引数は GL_COLOR_ATTACHMENTn_EXT, GL_DEPTH_ATTACHMENT_EXT, GL_STENCIL_ATTACHMENT_EXT など
            //GL.Ext.FramebufferRenderbuffer(FramebufferTarget.FramebufferExt, FramebufferAttachment.ColorAttachment0Ext, RenderbufferTarget.RenderbufferExt, rbID);

            //GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, 0);

            //GL.Ext.FramebufferTexture2D(FramebufferTarget.FramebufferExt, FramebufferAttachment.ColorAttachment0Ext, TextureTarget.Texture2D, texID, 0);
            //GL.Ext.FramebufferRenderbuffer(FramebufferTarget.FramebufferExt, FramebufferAttachment.DepthAttachmentExt, RenderbufferTarget.RenderbufferExt, rbID);
            #endregion


            Console.ReadKey();
        }
    }
    //640, 360
    public class Game : GameWindow {
        public Game() : base(GameWindowSettings.Default, NativeWindowSettings.Default) {
        }

        //ウィンドウの起動時に実行される。
        protected override void OnLoad() {
            base.OnLoad();

            GL.ClearColor(Color4.Black);
            GL.Enable(EnableCap.DepthTest);
        }

        //ウィンドウのサイズが変更された場合に実行される。
        protected override void OnResize(ResizeEventArgs e) {
            base.OnResize(e);

            GL.Viewport(0, 0, 640, 360);
            GL.MatrixMode(MatrixMode.Projection);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 4, 640f / 360f, 1.0f, 64.0f);
            GL.LoadMatrix(ref projection);
        }

        //画面描画で実行される。
        protected override void OnRenderFrame(FrameEventArgs e) {
            base.OnRenderFrame(e);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.MatrixMode(MatrixMode.Modelview);
            Matrix4 modelview = Matrix4.LookAt(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY);
            GL.LoadMatrix(ref modelview);

            GL.Begin(PrimitiveType.Quads);

            GL.Color4(Color4.White);                            //色名で指定
            GL.Vertex3(-10f, 10f, 40f);
            GL.Color4(new float[] { 1.0f, 0.0f, 0.0f, 1.0f });  //配列で指定
            GL.Vertex3(-10f, -10f, 40f);
            GL.Color4(0.0f, 1.0f, 0.0f, 1.0f);                  //4つの引数にfloat型で指定
            GL.Vertex3(10f, -10f, 40f);
            GL.Color4((byte)0, (byte)0, (byte)255, (byte)255);  //byte型で指定
            GL.Vertex3(10f, 10f, 40f);

            GL.End();

            SwapBuffers();
        }
    }
}
