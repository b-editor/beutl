using System;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

using Veldrid;
using Veldrid.OpenGLBinding;
using Veldrid.Sdl2;
using Veldrid.SPIRV;
using Veldrid.StartupUtilities;
using Veldrid.Utilities;

using TextureVeldrid = Veldrid.Texture;

namespace BEditor.Graphics.Veldrid
{
    public unsafe sealed class GraphicsContextImpl : IGraphicsContextImpl
    {
        private readonly Sdl2Window _window;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly DisposeCollectorResourceFactory _factory;
        private readonly DisposeCollectorResourceFactory _swapchainfactory;
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

            _factory = new DisposeCollectorResourceFactory(_graphicsDevice.ResourceFactory);

            _swapchainfactory = new DisposeCollectorResourceFactory(_graphicsDevice.ResourceFactory);

            // シェーダーを作成
            _lightShader = ReadShader("lighting");
            _texLightShader = ReadShader("lighting_texture");
            _lineShader = ReadShader("line");
            _shader = ReadShader("shader");
            _textureShader = ReadShader("texture");

            // コマンドリスト作成
            _commandList = _factory.CreateCommandList();

            // オフスクリーン設定
            _stage = _factory.CreateTexture(TextureDescription.Texture2D(
                (uint)width,
                (uint)height,
                1,
                1,
                PixelFormat.B8_G8_R8_A8_UNorm,
                TextureUsage.Staging));

            _offscreenColor = _factory.CreateTexture(TextureDescription.Texture2D(
                (uint)width, (uint)height, 1, 1,
                 PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));

            _offscreenView = _factory.CreateTextureView(_offscreenColor);

            _offscreenDepth = _factory.CreateTexture(TextureDescription.Texture2D(
                (uint)width, (uint)height, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));

            _offscreenFB = _factory.CreateFramebuffer(new FramebufferDescription(_offscreenDepth, _offscreenColor));

            _projectionBuffer = _factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            _viewBuffer = _factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            _worldBuffer = _factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

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
                _factory.DisposeCollector.DisposeAll();
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
            // VertexBuffer
            var vertex = line.Vertices;
            var vertexBuffer = _swapchainfactory.CreateBuffer(new BufferDescription((uint)(sizeof(float) * vertex.Length), BufferUsage.VertexBuffer));
            _graphicsDevice.UpdateBuffer(vertexBuffer, 0, vertex);

            // ColorBuffer
            var colorBuffer = _swapchainfactory.CreateBuffer(new BufferDescription((uint)sizeof(RgbaFloat), BufferUsage.UniformBuffer));
            _graphicsDevice.UpdateBuffer(colorBuffer, 0, line.Color.ToFloat());

            // ShaderSet
            var shaderSet = new ShaderSetDescription(
                new[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                },
                _lineShader);

            // Layout
            var projViewLayout = _swapchainfactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            var worldTextureLayout = _swapchainfactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // Pipeline
            var pipeline = _swapchainfactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                line.BlendMode.ToBlendStateDescription(),
                DepthTestState.ToDepthStencilStateDescription(),
                RasterizerStateDescription.Default,
                PrimitiveTopology.LineList,
                shaderSet,
                new[] { projViewLayout, worldTextureLayout },
                _offscreenFB.OutputDescription));

            // ResourceSet
            var projViewSet = _swapchainfactory.CreateResourceSet(new ResourceSetDescription(
                projViewLayout,
                _projectionBuffer,
                _viewBuffer));

            var worldTextureSet = _swapchainfactory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                _worldBuffer,
                colorBuffer));

            UpdateBuffer(Camera, line.Transform);

            // Draw
            _commandList.SetPipeline(pipeline);
            _commandList.SetVertexBuffer(0, vertexBuffer);
            _commandList.SetGraphicsResourceSet(0, projViewSet);
            _commandList.SetGraphicsResourceSet(1, worldTextureSet);
            _commandList.Draw(2, 1, 0, 0);
        }

        public void DrawTexture(Texture texture)
        {
            if (texture.PlatformImpl is TextureImpl impl)
            {
                var tex = ToTexture(impl);
                var view = _swapchainfactory.CreateTextureView(tex);

                // VertexBuffer
                using var vertex = ToVertexTextureArray(texture.Vertices);
                var vertexSize = (uint)(sizeof(VertexPositionTexture) * vertex.Length);
                var vertexBuffer = _swapchainfactory.CreateBuffer(new BufferDescription(vertexSize, BufferUsage.VertexBuffer));
                _graphicsDevice.UpdateBuffer(vertexBuffer, 0, vertex.Pointer, vertexSize);

                // IndexBuffer
                using var indices = new UnmanagedArray<ushort>(6)
                {
                    [0] = 0,
                    [1] = 1,
                    [2] = 2,
                    [3] = 0,
                    [4] = 2,
                    [5] = 3,
                };
                var indicesSize = sizeof(ushort) * (uint)indices.Length;
                var indexBuffer = _swapchainfactory.CreateBuffer(new BufferDescription(indicesSize, BufferUsage.IndexBuffer));
                _graphicsDevice.UpdateBuffer(indexBuffer, 0, indices.Pointer, indicesSize);

                // ColorBuffer
                var colorBuffer = _swapchainfactory.CreateBuffer(new BufferDescription((uint)sizeof(RgbaFloat), BufferUsage.UniformBuffer));
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
                var projViewLayout = _swapchainfactory.CreateResourceLayout(
                    new ResourceLayoutDescription(
                        new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                        new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

                var worldTextureLayout = _swapchainfactory.CreateResourceLayout(
                    new ResourceLayoutDescription(
                        new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                        new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                        new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                        new ResourceLayoutElementDescription("ColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

                // Pipeline
                var pipeline = _swapchainfactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                    texture.BlendMode.ToBlendStateDescription(),
                    DepthTestState.ToDepthStencilStateDescription(),
                    RasterizerStateDescription.Default,
                    PrimitiveTopology.TriangleList,
                    shaderSet,
                    new[] { projViewLayout, worldTextureLayout },
                    _offscreenFB.OutputDescription));

                // ResourceSet
                var projViewSet = _swapchainfactory.CreateResourceSet(new ResourceSetDescription(
                    projViewLayout,
                    _projectionBuffer,
                    _viewBuffer));

                var worldTextureSet = _swapchainfactory.CreateResourceSet(new ResourceSetDescription(
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
            _commandList.CopyTexture(_offscreenFB.ColorTargets[0].Target, _stage);

            _commandList.End();

            _graphicsDevice.SubmitCommands(_commandList);
            _graphicsDevice.WaitForIdle();

            var buf = _graphicsDevice.Map(_stage, MapMode.Read);
            fixed (BGRA32* dst = image.Data)
            {
                var size = image.DataSize;
                Buffer.MemoryCopy((void*)buf.Data, dst, size, size);
            }

            _graphicsDevice.Unmap(_stage);

            _swapchainfactory.DisposeCollector.DisposeAll();

            _commandList.Begin();
        }

        public void SetSize(Size size)
        {
            _window.Width = Width = size.Width;
            _window.Height = Height = size.Height;

            // Dispose
            var collector = _factory.DisposeCollector;
            _stage.Dispose();
            _offscreenColor.Dispose();
            _offscreenView.Dispose();
            _offscreenDepth.Dispose();
            _offscreenFB.Dispose();
            collector.Remove(_stage);
            collector.Remove(_offscreenColor);
            collector.Remove(_offscreenView);
            collector.Remove(_offscreenDepth);
            collector.Remove(_offscreenFB);

            // オフスクリーン設定
            _stage = _factory.CreateTexture(TextureDescription.Texture2D(
                (uint)Width,
                (uint)Height,
                1,
                1,
                PixelFormat.B8_G8_R8_A8_UNorm,
                TextureUsage.Staging));

            _offscreenColor = _factory.CreateTexture(TextureDescription.Texture2D(
                (uint)Width, (uint)Height, 1, 1,
                 PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.RenderTarget | TextureUsage.Sampled));

            _offscreenView = _factory.CreateTextureView(_offscreenColor);

            _offscreenDepth = _factory.CreateTexture(TextureDescription.Texture2D(
                (uint)Width, (uint)Height, 1, 1, PixelFormat.R16_UNorm, TextureUsage.DepthStencil));

            _offscreenFB = _factory.CreateFramebuffer(new FramebufferDescription(_offscreenDepth, _offscreenColor));
        }

        private static UnmanagedArray<VertexPositionTexture> ToVertexTextureArray(VertexPositionTexture[] array)
        {
            return new UnmanagedArray<VertexPositionTexture>(4)
            {
                [0] = array[2],
                [1] = array[1],
                [2] = array[0],
                [3] = array[3],
            };
        }

        private unsafe TextureVeldrid ToTexture(TextureImpl impl)
        {
            var tex = _swapchainfactory.CreateTexture(impl.ToTextureDescription());
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

            return _factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);
        }
    }
}