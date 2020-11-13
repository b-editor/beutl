using System;

using OpenTK.Graphics.OpenGL;
using OpenTK.Graphics;
using OpenCvSharp;
using OpenTK;
using OpenTK.Input;
using OpenTK.Mathematics;
using System.Runtime.InteropServices;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Diagnostics;
using System.Text;
using OpenTK.Windowing.Desktop;

namespace OpenTKTest
{
    unsafe class Program
    {
        const int width = 500;
        const int height = 500;

        static int fbID, rbID, texID;
        const int FB_WIDTH = 500, FB_HEIGHT = 500;
        private const string LibraryName = "glfw3.dll";

        [DllImport(LibraryName)]
        public static extern int glfwInit();

        [DllImport(LibraryName)]
        public static extern OpenTK.Windowing.GraphicsLibraryFramework.Window* glfwCreateWindow(int width, int height, byte* title, Monitor* monitor, OpenTK.Windowing.GraphicsLibraryFramework.Window* share);

        [DllImport(LibraryName)]
        public static extern void glfwWindowHint(WindowHintBool hint, int value);

        [DllImport(LibraryName)]
        public static extern void glfwMakeContextCurrent(OpenTK.Windowing.GraphicsLibraryFramework.Window* window);

        static void Main(string[] args)
        {
            using (GameWindow aa = new GameWindow(GameWindowSettings.Default, NativeWindowSettings.Default)) ;

            glfwInit();
            glfwWindowHint(WindowHintBool.Visible, 0);

            var window = glfwCreateWindow(width, height, UTF8Encode("aaa"), null, null);
            glfwMakeContextCurrent(window);


            #region Renderbuffer

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

            #endregion

            GL.Ext.BindFramebuffer(FramebufferTarget.FramebufferExt, fbID);


            GL.ClearColor(Color4.Black);
            GL.Enable(EnableCap.DepthTest);

            GL.Viewport(0, 0, width, height);
            GL.MatrixMode(MatrixMode.Projection);
            Matrix4 projection = Matrix4.CreatePerspectiveFieldOfView((float)Math.PI / 4, width / height, 1.0f, 64.0f);
            GL.LoadMatrix(ref projection);

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            GL.MatrixMode(MatrixMode.Modelview);
            Matrix4 modelview = Matrix4.LookAt(Vector3.Zero, Vector3.UnitZ, Vector3.UnitY);
            GL.LoadMatrix(ref modelview);

            GL.Begin(PrimitiveType.Quads);
            {
                GL.Color4(Color4.White);                            //色名で指定
                GL.Vertex3(-10f, 10f, 40f);

                GL.Color4(new float[] { 1.0f, 0.0f, 0.0f, 1.0f });  //配列で指定
                GL.Vertex3(-10f, -10f, 40f);

                GL.Color4(0.0f, 1.0f, 0.0f, 1.0f);                  //4つの引数にfloat型で指定
                GL.Vertex3(10f, -10f, 40f);

                GL.Color4((byte)0, (byte)0, (byte)255, (byte)255);  //byte型で指定
                GL.Vertex3(10f, 10f, 40f);
            }
            GL.End();


            GL.ReadBuffer(ReadBufferMode.Front);
            Mat mat = new Mat(height, width, MatType.CV_8UC4);
            GL.ReadPixels(0, 0, width, height, PixelFormat.Bgra, PixelType.UnsignedByte, mat.Data);

            mat.SaveImage(@"E:\yuuto\OneDrive\画像\Test.png");
        }


        internal static int UTF8Size(string str)
        {
            Debug.Assert(str != null);
            return (str.Length * 4) + 1;
        }
        internal static unsafe byte* UTF8Encode(string str)
        {
            Debug.Assert(str != null);
            int bufferSize = UTF8Size(str);
            byte* buffer = (byte*)Marshal.AllocHGlobal(bufferSize);
            fixed (char* strPtr = str)
            {
                Encoding.UTF8.GetBytes(strPtr, str.Length + 1, buffer, bufferSize);
            }
            return buffer;
        }
    }
}