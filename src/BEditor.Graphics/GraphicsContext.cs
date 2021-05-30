// GraphicsContext.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.ComponentModel;
using System.Numerics;
using System.Threading;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the graphics context.
    /// </summary>
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

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsContext"/> class.
        /// </summary>
        /// <param name="width">The width of the framebuffer.</param>
        /// <param name="height">The height of the framebuffer.</param>
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
            GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
            GLFW.WindowHint(WindowHintClientApi.ClientApi, ClientApi.OpenGlApi);
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 3);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            GLFW.WindowHint(WindowHintBool.Visible, false);
            _window = GLFW.CreateWindow(1, 1, string.Empty, null, null);
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

            Colorbuffer = new ColorBuffer(width, height, PixelInternalFormat.Rgba, PixelFormat.Bgra, PixelType.UnsignedByte);
            Depthbuffer = new DepthBuffer(width, height);
            Framebuffer = new(Colorbuffer, Depthbuffer);

            PixelBufferObject = new(width, height, 4, PixelFormat.Bgra, PixelType.UnsignedByte);

            Clear();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="GraphicsContext"/> class.
        /// </summary>
        ~GraphicsContext()
        {
            if (!IsDisposed) Dispose();
        }

        /// <summary>
        /// Gets the pixel buffer.
        /// </summary>
        public PixelBuffer PixelBufferObject { get; private set; }

        /// <summary>
        /// Gets the depth buffer.
        /// </summary>
        public DepthBuffer Depthbuffer { get; private set; }

        /// <summary>
        /// Gets the color buffer.
        /// </summary>
        public ColorBuffer Colorbuffer { get; private set; }

        /// <summary>
        /// Gets the frame buffer.
        /// </summary>
        public FrameBuffer Framebuffer { get; private set; }

        /// <summary>
        /// Gets the width of the frame buffer.
        /// </summary>
        public int Width { get; private set; }

        /// <summary>
        /// Gets the height of the frame buffer.
        /// </summary>
        public int Height { get; private set; }

        /// <summary>
        /// Gets the aspect ratio.
        /// </summary>
        public float Aspect => Width / ((float)Height);

        /// <summary>
        /// Gets a value indicating whether or not this graphics context is set to current.
        /// </summary>
        public bool IsCurrent => GLFW.GetCurrentContext() == _window;

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Gets or sets the camera.
        /// </summary>
        public Camera Camera { get; set; }

        /// <summary>
        /// Gets or sets the light.
        /// </summary>
        public Light? Light { get; set; }

        /// <summary>
        /// Sets the framebuffer size.
        /// </summary>
        /// <param name="size">The framebuffer size.</param>
        public void SetSize(Size size)
        {
            if (Width == size.Width && Height == size.Height)
            {
                Clear();
                return;
            }

            MakeCurrent();

            Width = size.Width;
            Height = size.Height;
            if (Camera is OrthographicCamera ortho)
            {
                ortho.Width = Width;
                ortho.Height = Height;
            }
            else if (Camera is PerspectiveCamera perspective)
            {
                perspective.AspectRatio = Aspect;
            }

            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            Colorbuffer.Dispose();
            Depthbuffer.Dispose();
            Framebuffer.Dispose();
            PixelBufferObject.Dispose();

            Colorbuffer = new ColorBuffer(Width, Height, PixelInternalFormat.Rgba, PixelFormat.Bgra, PixelType.UnsignedByte);
            Depthbuffer = new DepthBuffer(Width, Height);
            Framebuffer = new(Colorbuffer, Depthbuffer);

            PixelBufferObject = new(Width, Height, 4, PixelFormat.Bgra, PixelType.UnsignedByte);

            GL.Viewport(0, 0, Width, Height);

            Framebuffer.Bind();
        }

        /// <summary>
        /// Clears the framebuffer.
        /// </summary>
        public void Clear()
        {
            MakeCurrent();

            GL.Viewport(0, 0, Width, Height);
            Tool.ThrowGLError();

            Framebuffer.Bind();

            // アンチエイリアス
            GL.Enable(EnableCap.LineSmooth);
            Tool.ThrowGLError();

            GL.Enable(EnableCap.PolygonSmooth);
            Tool.ThrowGLError();

            GL.Hint(HintTarget.LineSmoothHint, HintMode.Nicest);
            Tool.ThrowGLError();

            GL.Hint(HintTarget.PolygonSmoothHint, HintMode.Nicest);
            Tool.ThrowGLError();

            GL.Hint(HintTarget.TextureCompressionHint, HintMode.Nicest);
            Tool.ThrowGLError();

            GL.Disable(EnableCap.DepthTest);
            Tool.ThrowGLError();

            GL.ClearColor(default);
            Tool.ThrowGLError();

            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            Tool.ThrowGLError();
        }

        /// <summary>
        /// Sets this graphics context to current.
        /// </summary>
        public void MakeCurrent()
        {
            if (!IsCurrent)
            {
                GLFW.MakeContextCurrent(_window);
                Tool.ThrowGLFWError();
            }
        }

        /// <summary>
        /// Make current and bind framebuffer.
        /// </summary>
        public void MakeCurrentAndBindFbo()
        {
            MakeCurrent();
            Framebuffer.Bind();
        }

        /// <summary>
        /// Draws the texture into the frame buffer.
        /// </summary>
        /// <param name="texture">The texture to be drawn.</param>
        public void DrawTexture(Texture texture)
        {
            if (texture is null) throw new ArgumentNullException(nameof(texture));

            MakeCurrentAndBindFbo();
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

        /// <summary>
        /// Draws the texture into the frame buffer.
        /// </summary>
        /// <param name="texture">The texture to be drawn.</param>
        /// <param name="blend">The function sets the blend mode.</param>
        public void DrawTexture(Texture texture, Action blend)
        {
            if (texture is null) throw new ArgumentNullException(nameof(texture));
            if (blend is null) throw new ArgumentNullException(nameof(blend));

            if (Light is null)
            {
                MakeCurrentAndBindFbo();

                texture.Use(TextureUnit.Texture0);

                _textureShader.Use();

                var vertexLocation = _textureShader.GetAttribLocation("aPosition");
                GL.EnableVertexAttribArray(vertexLocation);
                Tool.ThrowGLError();
                GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
                Tool.ThrowGLError();

                var texCoordLocation = _textureShader.GetAttribLocation("aTexCoord");
                GL.EnableVertexAttribArray(texCoordLocation);
                Tool.ThrowGLError();
                GL.VertexAttribPointer(texCoordLocation, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 3 * sizeof(float));
                Tool.ThrowGLError();

                _textureShader.SetInt("texture0", 0);

                GL.Enable(EnableCap.Blend);
                Tool.ThrowGLError();

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

        /// <summary>
        /// Draws the cube into the frame buffer.
        /// </summary>
        /// <param name="cube">The cube to be drawn.</param>
        public void DrawCube(Cube cube)
        {
            if (cube is null) throw new ArgumentNullException(nameof(cube));
            MakeCurrentAndBindFbo();

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

        /// <summary>
        /// Draws the ball into the frame buffer.
        /// </summary>
        /// <param name="ball">The ball to be drawn.</param>
        public void DrawBall(Ball ball)
        {
            if (ball is null) throw new ArgumentNullException(nameof(ball));
            MakeCurrentAndBindFbo();

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

        /// <summary>
        /// Draws the line into the frame buffer.
        /// </summary>
        /// <param name="start">The starting coordinates of the line.</param>
        /// <param name="end">The ending coordinates of the line.</param>
        /// <param name="width">The width of the line.</param>
        /// <param name="transform">The transformation matrix for drawing the line.</param>
        /// <param name="color">The color of the line.</param>
        public void DrawLine(Vector3 start, Vector3 end, float width, Transform transform, Color color)
        {
            MakeCurrentAndBindFbo();

            using var line = new Line(start, end, width)
            {
                Transform = transform,
                Color = color,
            };

            DrawLine(line);
        }

        /// <summary>
        /// Draws the line into the frame buffer.
        /// </summary>
        /// <param name="line">The line to be drawn.</param>
        public void DrawLine(Line line)
        {
            if (line is null) throw new ArgumentNullException(nameof(line));

            MakeCurrentAndBindFbo();

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

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            _synchronization.Send(state =>
            {
                var g = (GraphicsContext)state!;

                g.PixelBufferObject.Dispose();
                g.Framebuffer.Dispose();
                g.Colorbuffer.Dispose();
                g.Depthbuffer.Dispose();

                GLFW.DestroyWindow(g._window);

                _lightShader.Dispose();
                _lineShader.Dispose();
                _shader.Dispose();
                _texLightShader.Dispose();
                _textureShader.Dispose();
            }, this);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        /// <summary>
        /// Reads an image from the frame buffer.
        /// </summary>
        /// <param name="image">The image to write the frame buffer pixels.</param>
        public void ReadFromFramebuffer(Image<BGRA32> image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            MakeCurrentAndBindFbo();

            fixed (BGRA32* data = image.Data)
            {
                PixelBufferObject.ReadPixelsFromTexture(Framebuffer.ColorObject.Handle, (IntPtr)data);
            }

            image.Flip(FlipMode.X);

            Tool.ThrowGLError();
        }

        /// <summary>
        /// Reads an image.
        /// </summary>
        /// <param name="image">The image to write the frame buffer pixels.</param>
        public void ReadImage(Image<BGRA32> image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            MakeCurrentAndBindFbo();

            GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

            fixed (BGRA32* data = image.Data)
            {
                GL.ReadPixels(0, 0, image.Width, image.Height, PixelFormat.Bgra, PixelType.UnsignedByte, (IntPtr)data);
            }

            image.Flip(FlipMode.X);
        }

        private void DrawTextureWithLight(Texture texture, Action blend)
        {
            if (texture is null) throw new ArgumentNullException(nameof(texture));
            if (blend is null) throw new ArgumentNullException(nameof(blend));
            MakeCurrentAndBindFbo();

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

            // InvalidEnum
            // GL.Enable(EnableCap.Texture2D);
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