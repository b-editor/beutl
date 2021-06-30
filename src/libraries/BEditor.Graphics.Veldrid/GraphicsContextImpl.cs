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
        private readonly VeldridPlatform _platform;
        private readonly DisposeCollectorResourceFactory _factory;

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

        public GraphicsContextImpl(int width, int height, VeldridPlatform platform)
        {
            _platform = platform;
            Width = width;
            Height = height;
            Camera = new OrthographicCamera(new Vector3(0, 0, 1024), width, height);

            _factory = new DisposeCollectorResourceFactory(GraphicsDevice.ResourceFactory);

            SwapchainFactory = new DisposeCollectorResourceFactory(GraphicsDevice.ResourceFactory);

            // シェーダーを作成
            _lightShader = ReadShader("lighting");
            _texLightShader = ReadShader("lighting_texture");
            _lineShader = ReadShader("line");
            _shader = ReadShader("shader");
            _textureShader = ReadShader("texture");

            // コマンドリスト作成
            CommandList = _factory.CreateCommandList();

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

            Framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(_offscreenDepth, _offscreenColor));

            ProjectionBuffer = _factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            ViewBuffer = _factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));
            WorldBuffer = _factory.CreateBuffer(new BufferDescription(64, BufferUsage.UniformBuffer));

            CommandList.Begin();
            Clear();
        }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool IsDisposed { get; private set; }

        public Camera Camera { get; set; }

        public Light? Light { get; set; }

        public DisposeCollectorResourceFactory SwapchainFactory { get; }

        public GraphicsDevice GraphicsDevice => _platform.GraphicsDevice;

        public CommandList CommandList { get; }

        public Framebuffer Framebuffer { get; private set; }

        public DeviceBuffer ProjectionBuffer { get; }

        public DeviceBuffer ViewBuffer { get; }

        public DeviceBuffer WorldBuffer { get; }

        public DepthStencilState DepthStencilState { get; set; } = DepthStencilState.Disabled;

        public void Clear()
        {
            CommandList.SetFramebuffer(Framebuffer);
            CommandList.ClearColorTarget(0, default);
            CommandList.ClearDepthStencil(1f);
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
            CommandList.SetFramebuffer(Framebuffer);
            using var impl = ball.ToImpl();
            // VertexBuffer
            var vertex = impl.Vertices;
            var vertexSize = (uint)(sizeof(float) * vertex.Length);
            var vertexBuffer = SwapchainFactory.CreateBuffer(new BufferDescription(vertexSize, BufferUsage.VertexBuffer));
            CommandList.UpdateBuffer(vertexBuffer, 0, vertex);

            // ColorBuffer
            var colorBuffer = SwapchainFactory.CreateBuffer(new BufferDescription((uint)sizeof(RgbaFloat), BufferUsage.UniformBuffer));
            CommandList.UpdateBuffer(colorBuffer, 0, ball.Color.ToFloat());

            // ShaderSet
            var shaderSet = new ShaderSetDescription(
                new[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                },
                _shader);

            // Layout
            var projViewLayout = SwapchainFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            var worldTextureLayout = SwapchainFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // Pipeline
            var pipeline = SwapchainFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                ball.BlendMode.ToBlendStateDescription(),
                DepthStencilState.ToVeldrid(),
                ball.RasterizerState.ToVeldrid(),
                PrimitiveTopology.TriangleStrip,
                shaderSet,
                new[] { projViewLayout, worldTextureLayout },
                Framebuffer.OutputDescription));

            // ResourceSet
            var projViewSet = SwapchainFactory.CreateResourceSet(new ResourceSetDescription(
                projViewLayout,
                ProjectionBuffer,
                ViewBuffer));

            var worldTextureSet = SwapchainFactory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                WorldBuffer,
                colorBuffer));

            UpdateBuffer(Camera, ball.Transform);

            // Draw
            CommandList.SetPipeline(pipeline);
            CommandList.SetVertexBuffer(0, vertexBuffer);
            CommandList.SetGraphicsResourceSet(0, projViewSet);
            CommandList.SetGraphicsResourceSet(1, worldTextureSet);
            CommandList.Draw((uint)vertex.Length, 1, 0, 0);
        }

        public void DrawCube(Cube cube)
        {
            CommandList.SetFramebuffer(Framebuffer);
            using var impl = cube.ToImpl();
            // VertexBuffer
            var vertex = impl.Vertices;
            var vertexSize = (uint)(sizeof(float) * vertex.Length);
            var vertexBuffer = SwapchainFactory.CreateBuffer(new BufferDescription(vertexSize, BufferUsage.VertexBuffer));
            CommandList.UpdateBuffer(vertexBuffer, 0, vertex);

            // IndexBuffer
            var indices = CubeImpl.GetCubeIndices();
            var indicesSize = sizeof(ushort) * (uint)indices.Length;
            var indexBuffer = SwapchainFactory.CreateBuffer(new BufferDescription(indicesSize, BufferUsage.IndexBuffer));
            CommandList.UpdateBuffer(indexBuffer, 0, indices);

            // ColorBuffer
            var colorBuffer = SwapchainFactory.CreateBuffer(new BufferDescription((uint)sizeof(RgbaFloat), BufferUsage.UniformBuffer));
            CommandList.UpdateBuffer(colorBuffer, 0, cube.Color.ToFloat());

            // ShaderSet
            var shaderSet = new ShaderSetDescription(
                new[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                },
                _shader);

            // Layout
            var projViewLayout = SwapchainFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            var worldTextureLayout = SwapchainFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // Pipeline
            var pipeline = SwapchainFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                cube.BlendMode.ToBlendStateDescription(),
                DepthStencilState.ToVeldrid(),
                cube.RasterizerState.ToVeldrid(),
                PrimitiveTopology.TriangleList,
                shaderSet,
                new[] { projViewLayout, worldTextureLayout },
                Framebuffer.OutputDescription));

            // ResourceSet
            var projViewSet = SwapchainFactory.CreateResourceSet(new ResourceSetDescription(
                projViewLayout,
                ProjectionBuffer,
                ViewBuffer));

            var worldTextureSet = SwapchainFactory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                WorldBuffer,
                colorBuffer));

            UpdateBuffer(Camera, cube.Transform);

            // Draw
            CommandList.SetPipeline(pipeline);
            CommandList.SetVertexBuffer(0, vertexBuffer);
            CommandList.SetIndexBuffer(indexBuffer, IndexFormat.UInt16);
            CommandList.SetGraphicsResourceSet(0, projViewSet);
            CommandList.SetGraphicsResourceSet(1, worldTextureSet);
            CommandList.DrawIndexed((uint)indices.Length, 1, 0, 0, 0);
        }

        public void DrawLine(Line line)
        {
            CommandList.SetFramebuffer(Framebuffer);
            // VertexBuffer
            var vertex = line.Vertices();
            var vertexBuffer = SwapchainFactory.CreateBuffer(new BufferDescription((uint)(sizeof(float) * vertex.Length), BufferUsage.VertexBuffer));
            CommandList.UpdateBuffer(vertexBuffer, 0, vertex);

            // ColorBuffer
            var colorBuffer = SwapchainFactory.CreateBuffer(new BufferDescription((uint)sizeof(RgbaFloat), BufferUsage.UniformBuffer));
            CommandList.UpdateBuffer(colorBuffer, 0, line.Color.ToFloat());

            // ShaderSet
            var shaderSet = new ShaderSetDescription(
                new[]
                {
                    new VertexLayoutDescription(
                        new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3))
                },
                _lineShader);

            // Layout
            var projViewLayout = SwapchainFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            var worldTextureLayout = SwapchainFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // Pipeline
            var pipeline = SwapchainFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                line.BlendMode.ToBlendStateDescription(),
                DepthStencilState.ToVeldrid(),
                line.RasterizerState.ToVeldrid(),
                PrimitiveTopology.LineList,
                shaderSet,
                new[] { projViewLayout, worldTextureLayout },
                Framebuffer.OutputDescription));

            // ResourceSet
            var projViewSet = SwapchainFactory.CreateResourceSet(new ResourceSetDescription(
                projViewLayout,
                ProjectionBuffer,
                ViewBuffer));

            var worldTextureSet = SwapchainFactory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                WorldBuffer,
                colorBuffer));

            UpdateBuffer(Camera, line.Transform);

            // Draw
            CommandList.SetPipeline(pipeline);
            CommandList.SetVertexBuffer(0, vertexBuffer);
            CommandList.SetGraphicsResourceSet(0, projViewSet);
            CommandList.SetGraphicsResourceSet(1, worldTextureSet);
            CommandList.Draw(2, 1, 0, 0);
        }

        public void DrawTexture(Texture texture)
        {
            CommandList.SetFramebuffer(Framebuffer);
            var tex = ToTexture(texture);
            var view = SwapchainFactory.CreateTextureView(tex);

            // VertexBuffer
            using var vertex = ToVertexTextureArray(texture.Vertices);
            var vertexSize = (uint)(sizeof(VertexPositionTexture) * vertex.Length);
            var vertexBuffer = SwapchainFactory.CreateBuffer(new BufferDescription(vertexSize, BufferUsage.VertexBuffer));
            CommandList.UpdateBuffer(vertexBuffer, 0, vertex.Pointer, vertexSize);

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
            var indexBuffer = SwapchainFactory.CreateBuffer(new BufferDescription(indicesSize, BufferUsage.IndexBuffer));
            CommandList.UpdateBuffer(indexBuffer, 0, indices.Pointer, indicesSize);

            // ColorBuffer
            var colorBuffer = SwapchainFactory.CreateBuffer(new BufferDescription((uint)sizeof(RgbaFloat), BufferUsage.UniformBuffer));
            CommandList.UpdateBuffer(colorBuffer, 0, texture.Color.ToFloat());

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
            var projViewLayout = SwapchainFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("ProjectionBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("ViewBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex)));

            var worldTextureLayout = SwapchainFactory.CreateResourceLayout(
                new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("WorldBuffer", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                    new ResourceLayoutElementDescription("SurfaceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("SurfaceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("ColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

            // Pipeline
            var pipeline = SwapchainFactory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                texture.BlendMode.ToBlendStateDescription(),
                DepthStencilState.ToVeldrid(),
                texture.RasterizerState.ToVeldrid(),
                PrimitiveTopology.TriangleList,
                shaderSet,
                new[] { projViewLayout, worldTextureLayout },
                Framebuffer.OutputDescription));

            // ResourceSet
            var projViewSet = SwapchainFactory.CreateResourceSet(new ResourceSetDescription(
                projViewLayout,
                ProjectionBuffer,
                ViewBuffer));

            var worldTextureSet = SwapchainFactory.CreateResourceSet(new ResourceSetDescription(
                worldTextureLayout,
                WorldBuffer,
                view,
                GraphicsDevice.Aniso4xSampler,
                colorBuffer));

            UpdateBuffer(Camera, texture.Transform);

            // Draw
            CommandList.SetPipeline(pipeline);
            CommandList.SetVertexBuffer(0, vertexBuffer);
            CommandList.SetIndexBuffer(indexBuffer, IndexFormat.UInt16);
            CommandList.SetGraphicsResourceSet(0, projViewSet);
            CommandList.SetGraphicsResourceSet(1, worldTextureSet);
            CommandList.DrawIndexed((uint)indices.Length, 1, 0, 0, 0);
        }

        public void MakeCurrent()
        {
        }

        public unsafe void ReadImage(Image<BGRA32> image)
        {
            CommandList.CopyTexture(Framebuffer.ColorTargets[0].Target, _stage);

            CommandList.End();

            GraphicsDevice.SubmitCommands(CommandList);
            GraphicsDevice.WaitForIdle();

            var buf = GraphicsDevice.Map(_stage, MapMode.Read);
            fixed (BGRA32* dst = image.Data)
            {
                var size = image.DataSize;
                Buffer.MemoryCopy((void*)buf.Data, dst, size, size);
            }

            GraphicsDevice.Unmap(_stage);

            SwapchainFactory.DisposeCollector.DisposeAll();

            CommandList.Begin();
        }

        public void SetSize(Size size)
        {
            Width = size.Width;
            Height = size.Height;

            // Dispose
            var collector = _factory.DisposeCollector;
            _stage.Dispose();
            _offscreenColor.Dispose();
            _offscreenView.Dispose();
            _offscreenDepth.Dispose();
            Framebuffer.Dispose();
            collector.Remove(_stage);
            collector.Remove(_offscreenColor);
            collector.Remove(_offscreenView);
            collector.Remove(_offscreenDepth);
            collector.Remove(Framebuffer);

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

            Framebuffer = _factory.CreateFramebuffer(new FramebufferDescription(_offscreenDepth, _offscreenColor));
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

        private unsafe TextureVeldrid ToTexture(Texture texture)
        {
            var tex = SwapchainFactory.CreateTexture(texture.ToTextureDescription());
            using var image = texture.ToImage();

            fixed (BGRA32* src = image.Data)
            {
                GraphicsDevice.UpdateTexture(tex, (IntPtr)src, (uint)image.DataSize, 0, 0, 0, (uint)image.Width, (uint)image.Height, 1, 0, 0);
            }

            return tex;
        }

        private void UpdateBuffer(Camera camera, Transform transform)
        {
            CommandList.UpdateBuffer(ProjectionBuffer, 0, camera.GetProjectionMatrix());
            CommandList.UpdateBuffer(ViewBuffer, 0, camera.GetViewMatrix());
            CommandList.UpdateBuffer(WorldBuffer, 0, transform.Matrix);
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