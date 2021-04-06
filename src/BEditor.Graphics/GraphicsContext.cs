using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private static bool _isFirst = true;
        private readonly Window* _window;
        private readonly Shader _textureShader;
        private readonly Shader _shader;
        private readonly Shader _lightShader;
        private readonly Shader _texLightShader;
        private readonly Shader _lineShader;
        private readonly SynchronizationContext _synchronization;

        public GraphicsContext(int width, int height)
        {
            Width = width;
            Height = height;
            _synchronization = AsyncOperationManager.SynchronizationContext;

            if (_isFirst)
            {
                GLFW.Init();
                Tool.ThrowGLFWError();
            }

            GLFW.DefaultWindowHints();
            GLFW.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGlApi);
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 3);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            GLFW.WindowHint(WindowHintBool.Visible, false);
            _window = GLFW.CreateWindow(1, 1, string.Empty, null, null);
            Tool.ThrowGLFWError();
            MakeCurrent();

            if (_isFirst)
            {
                var context = new GLFWBindingsContext();
                GL.LoadBindings(context);
                OpenTK.Graphics.OpenGL.GL.LoadBindings(context);
                OpenTK.Graphics.ES11.GL.LoadBindings(context);
                OpenTK.Graphics.ES20.GL.LoadBindings(context);
                OpenTK.Graphics.ES30.GL.LoadBindings(context);

                _isFirst = false;
            }

            _textureShader = ShaderFactory.Texture.Create();
            _shader = ShaderFactory.Default.Create();
            _lightShader = ShaderFactory.Lighting.Create();
            _texLightShader = ShaderFactory.TextureLighting.Create();
            _lineShader = ShaderFactory.Line.Create();

            Camera = new OrthographicCamera(new(0, 0, 1024), width, height);

            Tool.ThrowGLError();

            var colorBuf = new ColorBuffer(width, height, PixelInternalFormat.Rgba, PixelFormat.Bgra, PixelType.UnsignedByte);
            var depthBuf = new DepthBuffer(width, height);
            Framebuffer = new(colorBuf, depthBuf);

            Tool.ThrowGLError();

            PixelBufferObject = new(width, height, 4, PixelFormat.Bgra, PixelType.UnsignedByte);

            Tool.ThrowGLError();

            Clear();
        }

        ~GraphicsContext()
        {
            if (!IsDisposed) Dispose();
        }

        public PixelBuffer PixelBufferObject { get; }

        public FrameBuffer Framebuffer { get; }

        public int Width { get; }

        public int Height { get; }

        public float Aspect => Width / ((float)Height);

        public bool IsCurrent => GLFW.GetCurrentContext() == _window;

        public bool IsDisposed { get; private set; }

        public Camera Camera { get; set; }

        public Light? Light { get; set; }

        public void Clear()
        {
            MakeCurrent();

            GL.Viewport(0, 0, Width, Height);

            Framebuffer.Bind();

            // アンチエイリアス
            GL.Enable(EnableCap.LineSmooth);
            GL.Enable(EnableCap.PolygonSmooth);

            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);
            GL.Hint(HintTarget.TextureCompressionHint, HintMode.Nicest);
            Tool.ThrowGLError();

            GL.Disable(EnableCap.DepthTest);
            Tool.ThrowGLError();

            GL.ClearColor(default);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            Tool.ThrowGLError();
        }

        public void MakeCurrent()
        {
            if (!IsCurrent)
            {
                GLFW.MakeContextCurrent(_window);
                Tool.ThrowGLFWError();
            }
        }

        public void DrawTexture(Texture texture)
        {
            if (texture is null) throw new ArgumentNullException(nameof(texture));

            MakeCurrent();
            texture.Use(TextureUnit.Texture0);

            _textureShader.Use();

            var vertexLocation = _textureShader.GetAttribLocation("aPosition");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);

            var texCoordLocation = _textureShader.GetAttribLocation("aTexCoord");
            GL.EnableVertexAttribArray(texCoordLocation);
            GL.VertexAttribPointer(texCoordLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            _textureShader.SetInt("texture0", 0);

            GL.Enable(EnableCap.Blend);

            GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _textureShader.SetVector4("color", texture.Color.ToVector4());
            _textureShader.SetMatrix4("model", texture.Transform.Matrix);
            _textureShader.SetMatrix4("view", Camera.GetViewMatrix());
            _textureShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

            _textureShader.Use();

            texture.Draw(TextureUnit.Texture0);

            Tool.ThrowGLError();
        }

        public void DrawTexture(Texture texture, Action blend)
        {
            if (texture is null) throw new ArgumentNullException(nameof(texture));
            if (blend is null) throw new ArgumentNullException(nameof(blend));

            if (Light is null)
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

                _textureShader.SetInt("texture0", 0);

                GL.Enable(EnableCap.Blend);

                blend();

                _textureShader.SetVector4("color", texture.Color.ToVector4());
                _textureShader.SetMatrix4("model", texture.Transform.Matrix);
                _textureShader.SetMatrix4("view", Camera.GetViewMatrix());
                _textureShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

                _textureShader.Use();

                texture.Draw(TextureUnit.Texture0);

                Tool.ThrowGLError();
            }
            else
            {
                DrawTextureWithLight(texture, blend);
            }
        }

        public void DrawCube(Cube cube)
        {
            if (cube is null) throw new ArgumentNullException(nameof(cube));
            MakeCurrent();

            if (Light is null)
            {
                _shader.Use();

                var vertexLocation = _shader.GetAttribLocation("aPos");
                GL.EnableVertexAttribArray(vertexLocation);
                GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);

                // blend
                GL.Enable(EnableCap.Blend);
                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                _shader.SetMatrix4("model", cube.Transform.Matrix);
                _shader.SetMatrix4("view", Camera.GetViewMatrix());
                _shader.SetMatrix4("projection", Camera.GetProjectionMatrix());
                _shader.SetVector4("color", cube.Color.ToVector4());

                _shader.Use();

                cube.Draw();

                Tool.ThrowGLError();
            }
            else
            {
                DrawCubeWithLight(cube);
            }
        }

        public void DrawBall(Ball ball)
        {
            if (ball is null) throw new ArgumentNullException(nameof(ball));
            MakeCurrent();

            if (Light is null)
            {
                _shader.Use();

                var vertexLocation = _shader.GetAttribLocation("aPos");
                GL.EnableVertexAttribArray(vertexLocation);
                GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

                GL.BindVertexArray(ball.VertexArrayObject);

                _shader.SetMatrix4("model", ball.Transform.Matrix);
                _shader.SetMatrix4("view", Camera.GetViewMatrix());
                _shader.SetMatrix4("projection", Camera.GetProjectionMatrix());
                _shader.SetVector4("color", ball.Color.ToVector4());

                ball.Draw();

                Tool.ThrowGLError();
            }
            else
            {
                DrawBallWithLight(ball);
            }
        }

        public void DrawLine(Vector3 start, Vector3 end, float width, Transform transform, Color color)
        {
            MakeCurrent();

            using var line = new Line(start, end, width)
            {
                Transform = transform,
                Color = color
            };

            DrawLine(line);
        }

        public void DrawLine(Line line)
        {
            if (line is null) throw new ArgumentNullException(nameof(line));

            MakeCurrent();

            _lineShader.Use();

            GL.Enable(EnableCap.Blend);

            GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            _lineShader.SetVector4("color", line.Color.ToVector4());
            _lineShader.SetMatrix4("model", line.Transform.Matrix);
            _lineShader.SetMatrix4("view", Camera.GetViewMatrix());
            _lineShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

            _lineShader.Use();

            line.Draw();

            Tool.ThrowGLError();
        }

        public void Dispose()
        {
            if (IsDisposed) return;

            _synchronization.Post(state =>
            {
                var g = (GraphicsContext)state!;

                PixelBufferObject.Dispose();

                GLFW.DestroyWindow(g._window);
                g._textureShader.Dispose();
                g._shader.Dispose();
                g._lightShader.Dispose();
            }, this);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        public void ReadImage(Image<BGRA32> image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            MakeCurrent();

            fixed (BGRA32* data = image.Data)
            {
                PixelBufferObject.ReadPixelsFromTexture(Framebuffer.ColorObject.Handle, (IntPtr)data);
            }

            image.Flip(FlipMode.X);

            Tool.ThrowGLError();
        }

        private void DrawTextureWithLight(Texture texture, Action blend)
        {
            if (texture is null) throw new ArgumentNullException(nameof(texture));
            if (blend is null) throw new ArgumentNullException(nameof(blend));
            MakeCurrent();

            texture.Use(TextureUnit.Texture0);

            _texLightShader.Use();

            var vertexLocation = _texLightShader.GetAttribLocation("aPosition");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);

            var texCoordLocation = _texLightShader.GetAttribLocation("aTexCoord");
            GL.EnableVertexAttribArray(texCoordLocation);
            GL.VertexAttribPointer(texCoordLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));

            var normalLocation = _texLightShader.GetAttribLocation("aNormal");
            GL.EnableVertexAttribArray(normalLocation);
            GL.VertexAttribPointer(normalLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);

            GL.BindVertexArray(texture.VertexArrayObject);

            _texLightShader.SetInt("texture0", 0);

            GL.Enable(EnableCap.Blend);

            blend();

            //InvalidEnum
            //GL.Enable(EnableCap.Texture2D);

            _texLightShader.SetMatrix4("model", texture.Transform.Matrix);
            _texLightShader.SetMatrix4("view", Camera.GetViewMatrix());
            _texLightShader.SetMatrix4("projection", Camera.GetProjectionMatrix());
            _texLightShader.SetVector3("viewPos", Camera.Position);
            _texLightShader.SetVector4("color", texture.Color.ToVector4());

            _texLightShader.SetVector4("material.ambient", texture.Material.Ambient.ToVector4());
            _texLightShader.SetVector4("material.diffuse", texture.Material.Diffuse.ToVector4());
            _texLightShader.SetVector4("material.specular", texture.Material.Specular.ToVector4());
            _texLightShader.SetFloat("material.shininess", texture.Material.Shininess);

            _texLightShader.SetVector3("light.position", Light!.Position);
            _texLightShader.SetVector4("light.ambient", Light.Ambient.ToVector4());
            _texLightShader.SetVector4("light.diffuse", Light.Diffuse.ToVector4());
            _texLightShader.SetVector4("light.specular", Light.Specular.ToVector4());

            _texLightShader.Use();

            texture.Draw(TextureUnit.Texture0);

            Tool.ThrowGLError();
        }

        private void DrawCubeWithLight(Cube cube)
        {
            if (cube is null) throw new ArgumentNullException(nameof(cube));
            _lightShader.Use();

            var vertexLocation = _lightShader.GetAttribLocation("aPos");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
            var normalLocation = _lightShader.GetAttribLocation("aNormal");
            GL.EnableVertexAttribArray(normalLocation);
            GL.VertexAttribPointer(normalLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));

            GL.BindVertexArray(cube.VertexArrayObject);

            _lightShader.SetMatrix4("model", cube.Transform.Matrix);
            _lightShader.SetMatrix4("view", Camera.GetViewMatrix());
            _lightShader.SetMatrix4("projection", Camera.GetProjectionMatrix());
            _lightShader.SetVector3("viewPos", Camera.Position);
            _lightShader.SetVector4("color", cube.Color.ToVector4());

            _lightShader.SetVector4("material.ambient", cube.Material.Ambient.ToVector4());
            _lightShader.SetVector4("material.diffuse", cube.Material.Diffuse.ToVector4());
            _lightShader.SetVector4("material.specular", cube.Material.Specular.ToVector4());
            _lightShader.SetFloat("material.shininess", cube.Material.Shininess);

            _lightShader.SetVector3("light.position", Light!.Position);
            _lightShader.SetVector4("light.ambient", Light.Ambient.ToVector4());
            _lightShader.SetVector4("light.diffuse", Light.Diffuse.ToVector4());
            _lightShader.SetVector4("light.specular", Light.Specular.ToVector4());

            cube.Draw();

            Tool.ThrowGLError();
        }

        private void DrawBallWithLight(Ball ball)
        {
            if (ball is null) throw new ArgumentNullException(nameof(ball));
            _lightShader.Use();

            var vertexLocation = _lightShader.GetAttribLocation("aPos");
            GL.EnableVertexAttribArray(vertexLocation);
            GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
            var normalLocation = _lightShader.GetAttribLocation("aNormal");
            GL.EnableVertexAttribArray(normalLocation);
            GL.VertexAttribPointer(normalLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));

            GL.BindVertexArray(ball.VertexArrayObject);

            _lightShader.SetMatrix4("model", ball.Transform.Matrix);
            _lightShader.SetMatrix4("view", Camera.GetViewMatrix());
            _lightShader.SetMatrix4("projection", Camera.GetProjectionMatrix());
            _lightShader.SetVector3("viewPos", Camera.Position);
            _lightShader.SetVector4("color", ball.Color.ToVector4());

            _lightShader.SetVector4("material.ambient", ball.Material.Ambient.ToVector4());
            _lightShader.SetVector4("material.diffuse", ball.Material.Diffuse.ToVector4());
            _lightShader.SetVector4("material.specular", ball.Material.Specular.ToVector4());
            _lightShader.SetFloat("material.shininess", ball.Material.Shininess);

            _lightShader.SetVector3("light.position", Light!.Position);
            _lightShader.SetVector4("light.ambient", Light.Ambient.ToVector4());
            _lightShader.SetVector4("light.diffuse", Light.Diffuse.ToVector4());
            _lightShader.SetVector4("light.specular", Light.Specular.ToVector4());

            ball.Draw();

            Tool.ThrowGLError();
        }
    }
}
