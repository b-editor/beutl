using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace BEditor.Graphics
{
    public unsafe sealed class GraphicsContext : IDisposable
    {
        private static bool isFirst = true;
        private readonly Window* _window;
        private readonly Shader _textureShader;
        private readonly Shader _shader;
        private readonly Shader _lightShader;
        private readonly Shader _lineShader;
        private readonly SynchronizationContext? _synchronization;

        public GraphicsContext(int width, int height)
        {
            Width = width;
            Height = height;
            _synchronization = SynchronizationContext.Current;

            if (isFirst)
            {
                GLFW.Init();
            }

            GLFW.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGlApi);
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 3);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            GLFW.WindowHint(WindowHintBool.Visible, false);
            _window = GLFW.CreateWindow(width, height, "", null, null);
            GLFW.SetWindowSizeLimits(_window, width, height, width, height);
            MakeCurrent();

            var error = GLFW.GetError(out var meg);

            if (isFirst)
            {
                var context = new GLFWBindingsContext();
                GL.LoadBindings(context);
                OpenTK.Graphics.OpenGL.GL.LoadBindings(context);
                OpenTK.Graphics.ES11.GL.LoadBindings(context);
                OpenTK.Graphics.ES20.GL.LoadBindings(context);
                OpenTK.Graphics.ES30.GL.LoadBindings(context);

                isFirst = false;
            }

            Clear();

            _textureShader = new(Shaders.TextureVert, Shaders.TextureFrag);
            _shader = new(Shaders.Vert, Shaders.Frag);
            _lightShader = new(Shaders.Vert, Shaders.LightFrag);
            _lineShader = new(Shaders.LineVert, Shaders.LineFrag);


            Camera = new OrthographicCamera(new(0, 0, 1024), width, height);
        }
        ~GraphicsContext()
        {
            if (!IsDisposed) Dispose();
        }

        public int Width { get; }
        public int Height { get; }
        public float Aspect => ((float)Width) / ((float)Height);
        public bool IsCurrent => GLFW.GetCurrentContext() == _window;
        public bool IsDisposed { get; private set; }
        public Camera Camera { get; set; }
        public Color ClearColor { get; set; }

        public void Clear()
        {
            MakeCurrent();

            GL.Viewport(0, 0, Width, Height);

            //法線の自動調節
            //GL.Enable(EnableCap.Normalize);
            //アンチエイリアス
            GL.Enable(EnableCap.LineSmooth);
            GL.Enable(EnableCap.PolygonSmooth);
            //GL.Enable(EnableCap.PointSmooth);

            GL.Hint(HintTarget.FogHint, HintMode.Nicest);
            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PerspectiveCorrectionHint, HintMode.Nicest);
            GL.Hint(HintTarget.PointSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);

            GL.Disable(EnableCap.DepthTest);
            //GL.Disable(EnableCap.Lighting);



            GL.ClearColor(ClearColor.ToOpenTK());
        }
        public void MakeCurrent()
        {
            try
            {
                if (!IsCurrent)
                {
                    GLFW.MakeContextCurrent(_window);
                }
            }
            catch
            {
                Debug.Assert(false);
            }
        }
        public void SwapBuffers()
        {
            GLFW.SwapBuffers(_window);
        }
        public void DrawTexture(Texture texture, Transform transform, Color color)
        {
            MakeCurrent();
            texture.Use(TextureUnit.Texture0);

            _textureShader.Use();

            var vertexLocation = _textureShader.GetAttribLocation("aPosition");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);

            var texCoordLocation = _textureShader.GetAttribLocation("aTexCoord");
            GL.EnableVertexAttribArray(texCoordLocation);
            GL.VertexAttribPointer(texCoordLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            _textureShader.SetInt("texture", 0);

            GL.Enable(EnableCap.Blend);


            GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.Enable(EnableCap.Texture2D);

            _textureShader.SetVector4("color", color.ToVector4());
            _textureShader.SetMatrix4("model", transform.Matrix);
            _textureShader.SetMatrix4("view", Camera.GetViewMatrix());
            _textureShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

            _textureShader.Use();

            texture.Render(TextureUnit.Texture0);
        }
        public void DrawTexture(Texture texture, Transform transform, Color color, Action blend)
        {
            MakeCurrent();

            texture.Use(TextureUnit.Texture0);

            _textureShader.Use();

            var vertexLocation = _textureShader.GetAttribLocation("aPosition");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);

            var texCoordLocation = _textureShader.GetAttribLocation("aTexCoord");
            GL.EnableVertexAttribArray(texCoordLocation);
            GL.VertexAttribPointer(texCoordLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            _textureShader.SetInt("texture", 0);

            GL.Enable(EnableCap.Blend);

            blend();

            GL.Enable(EnableCap.Texture2D);

            _textureShader.SetVector4("color", color.ToVector4());
            _textureShader.SetMatrix4("model", transform.Matrix);
            _textureShader.SetMatrix4("view", Camera.GetViewMatrix());
            _textureShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

            _textureShader.Use();

            texture.Render(TextureUnit.Texture0);
        }
        public void DrawCube(Cube cube, Transform transform)
        {
            MakeCurrent();

            _shader.Use();

            var vertexLocation = _shader.GetAttribLocation("aPos");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);


            GL.BindVertexArray(cube.VertexArrayObject);

            _shader.SetMatrix4("model", transform.Matrix);
            _shader.SetMatrix4("view", Camera.GetViewMatrix());
            _shader.SetMatrix4("projection", Camera.GetProjectionMatrix());
            _shader.SetVector4("color", cube.Color.ToVector4());

            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }
        public void DrawLine(Vector3 start, Vector3 end, float width, Transform transform, Color color)
        {
            MakeCurrent();

            using var line = new Line(start, end, width);

            _lineShader.Use();

            GL.Enable(EnableCap.Blend);


            GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _lineShader.SetVector4("color", color.ToVector4());
            _lineShader.SetMatrix4("model", transform.Matrix);
            _lineShader.SetMatrix4("view", Camera.GetViewMatrix());
            _lineShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

            _lineShader.Use();

            line.Render();
        }
        public void Dispose()
        {
            if (IsDisposed) return;

            _synchronization?.Post(state =>
            {
                var g = (GraphicsContext)state!;

                GLFW.DestroyWindow(g._window);
                g._textureShader.Dispose();
                g._shader.Dispose();
                g._lightShader.Dispose();

            }, this);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
        public unsafe void ReadImage(Image<BGRA32> image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            MakeCurrent();

            GL.ReadBuffer(ReadBufferMode.Front);

            fixed (BGRA32* data = image.Data)
            {
                GL.ReadPixels(0, 0, image.Width, image.Height, PixelFormat.Bgra, PixelType.UnsignedByte, (IntPtr)data);
            }

            image.Flip(FlipMode.X);
        }
    }
}
