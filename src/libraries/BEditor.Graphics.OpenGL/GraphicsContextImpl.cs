using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

using BEditor.Data;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

using Microsoft.Extensions.DependencyInjection;

using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace BEditor.Graphics.OpenGL
{
    /// <summary>
    /// Represents the graphics context.
    /// </summary>
    public unsafe sealed class GraphicsContextImpl : IGraphicsContextImpl
    {
        [AllowNull]
        internal static SynchronizationContext SyncContext;
        private static bool _isFirst = true;
        private Window* _window;
        private Shader _textureShader;
        private Shader _shader;
        private Shader _lightShader;
        private Shader _texLightShader;
        private Shader _lineShader;

#pragma warning disable CS8618
        public GraphicsContextImpl(int width, int height)
#pragma warning restore CS8618
        {
            Width = width;
            Height = height;
            SyncContext ??= ServicesLocator.Current.Provider.GetRequiredService<ITopLevel>().UIThread;
            SyncContext.Send(_ =>
            {
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
            }, null);
        }

        ~GraphicsContextImpl()
        {
            if (!IsDisposed) Dispose();
        }

        public PixelBuffer PixelBufferObject { get; private set; }

        public DepthBuffer Depthbuffer { get; private set; }

        public ColorBuffer Colorbuffer { get; private set; }

        public FrameBuffer Framebuffer { get; private set; }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public float Aspect => Width / ((float)Height);

        public bool IsCurrent => GLFW.GetCurrentContext() == _window;

        public bool IsDisposed { get; private set; }

        public Camera Camera { get; set; }

        public Light? Light { get; set; }

        /// <inheritdoc/>
        public DepthStencilState DepthStencilState { get; set; } = DepthStencilState.Disabled;

        public void SetSize(Size size)
        {
            SyncContext.Send(_ =>
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
            }, null);
        }

        public void Clear()
        {
            DepthStencilState = DepthStencilState.Disabled;
            SyncContext.Send(_ =>
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
            }, null);
        }

        public void MakeCurrent()
        {
            SyncContext.Send(_ =>
            {
                if (!IsCurrent)
                {
                    GLFW.MakeContextCurrent(_window);
                    Tool.ThrowGLFWError();
                }
            }, null);
        }

        public void MakeCurrentAndBindFbo()
        {
            MakeCurrent();
            SyncContext.Send(_ => Framebuffer.Bind(), null);
        }

        public void DrawTexture(Texture texture)
        {
            if (texture is null) throw new ArgumentNullException(nameof(texture));

            if (Light is null)
            {
                MakeCurrentAndBindFbo();

                SyncContext.Send(_ =>
                {
                    using var impl = texture.ToImpl();
                    impl.Use(TextureUnit.Texture0);

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

                    ApplyRasterizerState(texture.RasterizerState);
                    ApplyDepthStencilState(DepthStencilState);
                    GL.Enable(EnableCap.Blend);
                    SetBlend(texture.BlendMode);
                    Tool.ThrowGLError();

                    _textureShader.SetVector4("color", texture.Color.ToVector4());
                    _textureShader.SetMatrix4("model", texture.Transform.Matrix);
                    _textureShader.SetMatrix4("view", Camera.GetViewMatrix());
                    _textureShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

                    _textureShader.Use();

                    impl.Draw(TextureUnit.Texture0);

                    Tool.ThrowGLError();
                }, null);
            }
            else
            {
                DrawTextureWithLight(texture);
            }
        }

        public void DrawCube(Cube cube)
        {
            if (cube is null) throw new ArgumentNullException(nameof(cube));
            MakeCurrentAndBindFbo();

            if (Light is null)
            {
                SyncContext.Send(_ =>
                {
                    using var impl = cube.ToImpl();
                    _shader.Use();

                    var vertexLocation = _shader.GetAttribLocation("aPos");
                    GL.EnableVertexAttribArray(vertexLocation);
                    GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);

                    ApplyRasterizerState(cube.RasterizerState);
                    ApplyDepthStencilState(DepthStencilState);
                    GL.Enable(EnableCap.Blend);
                    SetBlend(cube.BlendMode);
                    Tool.ThrowGLError();

                    _shader.SetMatrix4("model", cube.Transform.Matrix);
                    _shader.SetMatrix4("view", Camera.GetViewMatrix());
                    _shader.SetMatrix4("projection", Camera.GetProjectionMatrix());
                    _shader.SetVector4("color", cube.Color.ToVector4());

                    _shader.Use();

                    impl.Draw();

                    Tool.ThrowGLError();
                }, null);
            }
            else
            {
                DrawCubeWithLight(cube);
            }
        }

        public void DrawBall(Ball ball)
        {
            if (ball is null) throw new ArgumentNullException(nameof(ball));
            MakeCurrentAndBindFbo();

            if (Light is null)
            {
                SyncContext.Send(_ =>
                {
                    using var impl = ball.ToImpl();
                    _shader.Use();

                    var vertexLocation = _shader.GetAttribLocation("aPos");
                    GL.EnableVertexAttribArray(vertexLocation);
                    GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);

                    GL.BindVertexArray(impl.VertexArrayObject);

                    ApplyRasterizerState(ball.RasterizerState);
                    ApplyDepthStencilState(DepthStencilState);
                    GL.Enable(EnableCap.Blend);
                    SetBlend(ball.BlendMode);
                    Tool.ThrowGLError();

                    _shader.SetMatrix4("model", ball.Transform.Matrix);
                    _shader.SetMatrix4("view", Camera.GetViewMatrix());
                    _shader.SetMatrix4("projection", Camera.GetProjectionMatrix());
                    _shader.SetVector4("color", ball.Color.ToVector4());

                    impl.Draw();

                    Tool.ThrowGLError();
                }, null);
            }
            else
            {
                DrawBallWithLight(ball);
            }
        }

        public void DrawLine(Line line)
        {
            if (line is null) throw new ArgumentNullException(nameof(line));

            MakeCurrentAndBindFbo();
            SyncContext.Send(_ =>
            {
                using var impl = line.ToImpl();

                _lineShader.Use();

                ApplyRasterizerState(line.RasterizerState);
                ApplyDepthStencilState(DepthStencilState);
                GL.Enable(EnableCap.Blend);

                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                _lineShader.SetVector4("color", line.Color.ToVector4());
                _lineShader.SetMatrix4("model", line.Transform.Matrix);
                _lineShader.SetMatrix4("view", Camera.GetViewMatrix());
                _lineShader.SetMatrix4("projection", Camera.GetProjectionMatrix());

                _lineShader.Use();

                impl.Draw();

                Tool.ThrowGLError();
            }, null);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            SyncContext.Send(state =>
            {
                var g = (GraphicsContextImpl)state!;

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

        public void ReadFromFramebuffer(Image<BGRA32> image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            MakeCurrentAndBindFbo();

            SyncContext.Send(_ =>
            {
                fixed (BGRA32* data = image.Data)
                {
                    PixelBufferObject.ReadPixelsFromTexture(Framebuffer.ColorObject.Handle, (IntPtr)data);
                }

                image.Flip(FlipMode.X);

                Tool.ThrowGLError();
            }, null);
        }

        public void ReadImage(Image<BGRA32> image)
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            MakeCurrentAndBindFbo();

            SyncContext.Send(_ =>
            {
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);

                fixed (BGRA32* data = image.Data)
                {
                    GL.ReadPixels(0, 0, image.Width, image.Height, PixelFormat.Bgra, PixelType.UnsignedByte, (IntPtr)data);
                }

                image.Flip(FlipMode.X);
            }, null);
        }

        void IGraphicsContextImpl.MakeCurrent()
        {
            MakeCurrentAndBindFbo();
        }

        private static void ApplyRasterizerState(RasterizerState state)
        {
            if (state.CullMode is FaceCullMode.None)
            {
                GL.Disable(EnableCap.CullFace);
            }
            else
            {
                GL.Enable(EnableCap.CullFace);
                GL.CullFace(state.CullMode is FaceCullMode.Back
                    ? CullFaceMode.Back
                    : CullFaceMode.Front);
            }

            if (state.ScissorTestEnabled) GL.Enable(EnableCap.ScissorTest);
            else GL.Disable(EnableCap.ScissorTest);

            GL.PolygonMode(MaterialFace.FrontAndBack, state.FillMode is PolygonFillMode.Solid ? PolygonMode.Fill : PolygonMode.Line);

            if (state.DepthClipEnabled) GL.Disable(EnableCap.DepthClamp);
            else GL.Enable(EnableCap.DepthClamp);

            GL.FrontFace(state.FrontFace is FrontFace.Clockwise
                ? FrontFaceDirection.Cw
                : FrontFaceDirection.Ccw);
        }

        private static void ApplyDepthStencilState(DepthStencilState state)
        {
            {
                if (state.DepthTestEnabled) GL.Enable(EnableCap.DepthTest);
                else GL.Disable(EnableCap.DepthTest);

                GL.DepthMask(state.DepthWriteEnabled);

                var func = state.DepthComparison switch
                {
                    ComparisonKind.Never => DepthFunction.Never,
                    ComparisonKind.Less => DepthFunction.Less,
                    ComparisonKind.Equal => DepthFunction.Equal,
                    ComparisonKind.LessEqual => DepthFunction.Lequal,
                    ComparisonKind.Greater => DepthFunction.Greater,
                    ComparisonKind.NotEqual => DepthFunction.Notequal,
                    ComparisonKind.GreaterEqual => DepthFunction.Gequal,
                    ComparisonKind.Always => DepthFunction.Always,
                    _ => DepthFunction.Less,
                };

                GL.DepthFunc(func);
            }

            {
                if (state.StencilTestEnabled) GL.Enable(EnableCap.StencilTest);
                else GL.Disable(EnableCap.StencilTest);

                // Todo: Stencil
            }
        }

        private static void SetBlend(BlendMode blend)
        {
            if (blend is BlendMode.AlphaBlend)
            {
                GL.BlendFuncSeparate(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha, BlendingFactorSrc.One, BlendingFactorDest.OneMinusSrcAlpha);
                GL.BlendEquation(BlendEquationMode.FuncAdd);
            }
            else if (blend is BlendMode.Additive)
            {
                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else if (blend is BlendMode.Subtract)
            {
                GL.BlendEquationSeparate(BlendEquationMode.FuncReverseSubtract, BlendEquationMode.FuncReverseSubtract);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
            }
            else if (blend is BlendMode.Multiplication)
            {
                GL.BlendEquationSeparate(BlendEquationMode.FuncAdd, BlendEquationMode.FuncAdd);
                GL.BlendFunc(BlendingFactor.Zero, BlendingFactor.SrcColor);
            }
        }

        private void DrawTextureWithLight(Texture texture)
        {
            MakeCurrentAndBindFbo();

            SyncContext.Send(_ =>
            {
                using var impl = texture.ToImpl();
                impl.Use(TextureUnit.Texture0);

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

                GL.BindVertexArray(impl.VertexArrayObject);

                _texLightShader.SetInt("texture0", 0);

                GL.Enable(EnableCap.Blend);
                SetBlend(texture.BlendMode);
                Tool.ThrowGLError();

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

                impl.Draw(TextureUnit.Texture0);

                Tool.ThrowGLError();
            }, null);
        }

        private void DrawCubeWithLight(Cube cube)
        {
            SyncContext.Send(_ =>
            {
                using var impl = cube.ToImpl();
                _lightShader.Use();

                var vertexLocation = _lightShader.GetAttribLocation("aPos");
                GL.EnableVertexAttribArray(vertexLocation);
                GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
                var normalLocation = _lightShader.GetAttribLocation("aNormal");
                GL.EnableVertexAttribArray(normalLocation);
                GL.VertexAttribPointer(normalLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));

                GL.BindVertexArray(impl.VertexArrayObject);

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

                impl.Draw();

                Tool.ThrowGLError();
            }, null);
        }

        private void DrawBallWithLight(Ball ball)
        {
            SyncContext.Send(_ =>
            {
                using var impl = ball.ToImpl();
                _lightShader.Use();

                var vertexLocation = _lightShader.GetAttribLocation("aPos");
                GL.EnableVertexAttribArray(vertexLocation);
                GL.VertexAttribPointer(vertexLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 0);
                var normalLocation = _lightShader.GetAttribLocation("aNormal");
                GL.EnableVertexAttribArray(normalLocation);
                GL.VertexAttribPointer(normalLocation, 3, VertexAttribPointerType.Float, false, 6 * sizeof(float), 3 * sizeof(float));

                GL.BindVertexArray(impl.VertexArrayObject);

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

                impl.Draw();

                Tool.ThrowGLError();
            }, null);
        }
    }
}