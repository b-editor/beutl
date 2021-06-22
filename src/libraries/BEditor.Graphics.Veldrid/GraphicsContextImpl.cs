using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

using Veldrid;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;

using TextureVeldrid = Veldrid.Texture;

namespace BEditor.Graphics.Veldrid
{
    public unsafe sealed class GraphicsContextImpl : IGraphicsContextImpl
    {
        private readonly Sdl2Window _window;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly CommandList _commandList;

        // シェーダー
        private readonly Shader[] _textureShader;
        private readonly Shader[] _shader;
        private readonly Shader[] _lightShader;
        private readonly Shader[] _texLightShader;
        private readonly Shader[] _lineShader;

        // オフスクリーン
        private TextureVeldrid _stage;
        private TextureVeldrid _offscreenColor;
        private TextureView _offscreenView;
        private TextureVeldrid _offscreenDepth;
        private Framebuffer _offscreenFB;

        // buffers
        private DeviceBuffer _projectionBuffer;
        private DeviceBuffer _viewBuffer;
        private DeviceBuffer _worldBuffer;

        public GraphicsContextImpl(int width, int height)
        {
            Width = width;
            Height = height;
            Camera = new OrthographicCamera(new Vector3(0, 0, 1024), width, height);

            var windowInfo = new WindowCreateInfo()
            {
                WindowWidth = width,
                WindowHeight = height,
                WindowInitialState = WindowState.Hidden,
            };
            _window = VeldridStartup.CreateWindow(ref windowInfo);

            var options = new GraphicsDeviceOptions
            {
                PreferStandardClipSpaceYDirection = true,
                PreferDepthRangeZeroToOne = true
            };

            _graphicsDevice = VeldridStartup.CreateGraphicsDevice(_window, options, GraphicsBackend.Vulkan);
            var factory = _graphicsDevice.ResourceFactory;

            // シェーダーを作成
            _lightShader = ReadShader("lighting");
            _texLightShader = ReadShader("lighting_texture");
            _lineShader = ReadShader("line");
            _shader = ReadShader("shader");
            _textureShader = ReadShader("texture");

            // コマンドリスト作成
            _commandList = factory.CreateCommandList();

            // オフスクリーン設定
            _stage = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)width,
                (uint)height,
                1,
                1,
                PixelFormat.B8_G8_R8_A8_UNorm,
                TextureUsage.Staging));

            _offscreenColor = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)width, (uint)height, 1, 1,
                 PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));

            _offscreenView = factory.CreateTextureView(_offscreenColor);

            _offscreenDepth = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)width, (uint)height, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));

            _offscreenFB = factory.CreateFramebuffer(new FramebufferDescription(_offscreenDepth, _offscreenColor));

            //_mvpBuffer = factory.CreateBuffer(new BufferDescription((uint)sizeof(ModelViewProjection), BufferUsage.UniformBuffer));

            _projectionBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            _viewBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            _worldBuffer = factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            _commandList.Begin();
            Clear();
        }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool IsDisposed { get; private set; }

        public Camera Camera { get; set; }

        public Light? Light { get; set; }

        public DepthTestState DepthTestState { get; set; }

        public void Clear()
        {
            _commandList.SetFramebuffer(_offscreenFB);
            _commandList.ClearColorTarget(0, default);
            _commandList.ClearDepthStencil(1f);
            DepthTestState = new(false, false, ComparisonKind.Less);
        }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                // フレームバッファ
                _stage.Dispose();
                _offscreenColor.Dispose();
                _offscreenView.Dispose();
                _offscreenDepth.Dispose();
                _offscreenFB.Dispose();

                // シェーダー
                _textureShader.Dispose();
                _shader.Dispose();
                _lightShader.Dispose();
                _texLightShader.Dispose();
                _lineShader.Dispose();

                //_mvpBuffer.Dispose();

                _commandList.Dispose();
                _graphicsDevice.Dispose();

                IsDisposed = true;
                GC.SuppressFinalize(this);
            }
        }

        public void DrawBall(Ball ball)
        {
            throw new NotImplementedException();
        }

        public void DrawCube(Cube cube)
        {
            throw new NotImplementedException();
        }

        public void DrawLine(Line line)
        {
        }

        public void DrawTexture(Texture texture)
        {
            if (texture.PlatformImpl is TextureImpl impl)
            {
                var factory = _graphicsDevice.ResourceFactory;
                var tex = ToTexture(impl);
                var view = factory.CreateTextureView(tex);

                // VertexBuffer
                var vertex = ToVertexTextureArray(texture.Vertices);
                var vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(sizeof(VertexPositionTexture) * /*impl.Vertices.Length*/vertex.Length), BufferUsage.VertexBuffer));
                _graphicsDevice.UpdateBuffer(vertexBuffer, 0, /*impl.Vertices.ToArray()*/vertex);

                // IndexBuffer
                ushort[] indices =
                {
                    0, 1, 2,
                    0, 2, 3,
                };
                var indexBuffer = factory.CreateBuffer(new BufferDescription(sizeof(ushort) * (uint)indices.Length, BufferUsage.IndexBuffer));
                _graphicsDevice.UpdateBuffer(indexBuffer, 0, indices);

                // ColorBuffer
                var colorBuffer = factory.CreateBuffer(new BufferDescription((uint)sizeof(RgbaFloat), BufferUsage.UniformBuffer));
                _graphicsDevice.UpdateBuffer(colorBuffer, 0, texture.Color.ToFloat());

                // ShaderSet
                var shaderSet = new ShaderSetDescription(
                    new[]
                    {
                        new VertexLayoutDescription(
                            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                            new VertexElementDescription("TexCoords", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))
                    },
                    _textureShader);

                // Layout
                var projViewLayout = factory.CreateResourceLayout(
                    new ResourceLayoutDescription(
                        new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                        new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

                var worldTextureLayout = factory.CreateResourceLayout(
                    new ResourceLayoutDescription(
                        new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                        new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                        new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                        new ResourceLayoutElementDescription("ColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

                // Pipeline
                var pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                    texture.BlendMode.ToBlendStateDescription(),
                    DepthTestState.ToDepthStencilStateDescription(),
                    RasterizerStateDescription.Default,
                    PrimitiveTopology.TriangleList,
                    shaderSet,
                    new[] { projViewLayout, worldTextureLayout },
                    _offscreenFB.OutputDescription));

                // ResourceSet
                var projViewSet = factory.CreateResourceSet(new ResourceSetDescription(
                    projViewLayout,
                    _projectionBuffer,
                    _viewBuffer));

                var worldTextureSet = factory.CreateResourceSet(new ResourceSetDescription(
                    worldTextureLayout,
                    _worldBuffer,
                    view,
                    _graphicsDevice.Aniso4xSampler,
                    colorBuffer));

                UpdateBuffer(Camera, impl.Transform);

                // Draw
                _commandList.SetPipeline(pipeline);
                _commandList.SetVertexBuffer(0, vertexBuffer);
                _commandList.SetIndexBuffer(indexBuffer, IndexFormat.UInt16);
                _commandList.SetGraphicsResourceSet(0, projViewSet);
                _commandList.SetGraphicsResourceSet(1, worldTextureSet);
                _commandList.DrawIndexed((uint)indices.Length, 1, 0, 0, 0);
            }
        }

        public void MakeCurrent()
        {
        }

        public unsafe void ReadImage(Image<BGRA32> image)
        {
            _commandList.CopyTexture(
                _offscreenFB.ColorTargets[0].Target, 0, 0, 0, 0, 0,
                _stage, 0, 0, 0, 0, 0,
                _stage.Width, _stage.Height, 1, 1);

            _commandList.End();

            _graphicsDevice.SubmitCommands(_commandList);
            _graphicsDevice.SwapBuffers();
            _graphicsDevice.WaitForIdle();

            var buf = _graphicsDevice.Map(_stage, MapMode.Read);
            fixed (BGRA32* dst = image.Data)
            {
                var size = image.DataSize;
                Buffer.MemoryCopy((void*)buf.Data, dst, size, size);
            }

            _graphicsDevice.Unmap(_stage);

            _commandList.Begin();
        }

        public void SetSize(Size size)
        {
            var factory = _graphicsDevice.ResourceFactory;

            _window.Width = Width = size.Width;
            _window.Height = Height = size.Height;

            // Dispose
            _stage.Dispose();
            _offscreenColor.Dispose();
            _offscreenView.Dispose();
            _offscreenDepth.Dispose();
            _offscreenFB.Dispose();

            // オフスクリーン設定
            _stage = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)Width,
                (uint)Height,
                1,
                1,
                PixelFormat.B8_G8_R8_A8_UNorm,
                TextureUsage.Staging));

            _offscreenColor = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)Width, (uint)Height, 1, 1,
                 PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));

            _offscreenView = factory.CreateTextureView(_offscreenColor);

            _offscreenDepth = factory.CreateTexture(TextureDescription.Texture2D(
                (uint)Width, (uint)Height, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));

            _offscreenFB = factory.CreateFramebuffer(new FramebufferDescription(_offscreenDepth, _offscreenColor));
        }

        private static VertexPositionTexture[] ToVertexTextureArray(ReadOnlyMemory<VertexPositionTexture> memory)
        {
            var span = memory.Span;
            return new VertexPositionTexture[]
            {
                span[2],
                span[1],
                span[0],
                span[3],
            };
        }

        private unsafe TextureVeldrid ToTexture(TextureImpl impl)
        {
            var factory = _graphicsDevice.ResourceFactory;
            var tex = factory.CreateTexture(impl.ToTextureDescription());
            using var image = impl.ToImage();

            fixed (BGRA32* src = image.Data)
            {
                _graphicsDevice.UpdateTexture(tex, (IntPtr)src, (uint)image.DataSize, 0, 0, 0, (uint)image.Width, (uint)image.Height, 1, 0, 0);
            }

            return tex;
        }

        private void UpdateBuffer(Camera camera, Transform transform)
        {
            _graphicsDevice.UpdateBuffer(_projectionBuffer, 0, camera.GetProjectionMatrix());
            _graphicsDevice.UpdateBuffer(_viewBuffer, 0, camera.GetViewMatrix());
            _graphicsDevice.UpdateBuffer(_worldBuffer, 0, transform.Matrix);
        }

        private Shader[] ReadShader(string name)
        {
            var asm = typeof(GraphicsContextImpl).Assembly;
            using var frag = asm.GetManifestResourceStream($"BEditor.Graphics.Veldrid.Resources.{name}.frag")!;
            using var fragReader = new StreamReader(frag);
            using var vert = asm.GetManifestResourceStream($"BEditor.Graphics.Veldrid.Resources.{name}.vert")!;
            using var vertReader = new StreamReader(vert);

            var fragCode = fragReader.ReadToEnd();
            var vertCode = vertReader.ReadToEnd();

            return CreateShader(vertCode, fragCode);
        }

        private Shader[] CreateShader(string vert, string frag)
        {
            var vertexShaderDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(vert),
                "main");
            var fragmentShaderDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(frag),
                "main");

            return _graphicsDevice.ResourceFactory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        }
    }
}
