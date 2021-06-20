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

        private readonly Shader[] _shaders;

        private const string VertexCode = @"
#version 450

layout(location = 0) in vec2 Position;
layout(location = 1) in vec4 Color;

layout(location = 0) out vec4 fsin_Color;

void main()
{
    gl_Position = vec4(Position, 0, 1);
    fsin_Color = Color;
}";

        private const string FragmentCode = @"
#version 450

layout(location = 0) in vec4 fsin_Color;
layout(location = 0) out vec4 fsout_Color;

void main()
{
    fsout_Color = fsin_Color;
}";

        // オフスクリーン
        private TextureVeldrid _stage;
        private TextureVeldrid _offscreenColor;
        private TextureView _offscreenView;
        private TextureVeldrid _offscreenDepth;
        private Framebuffer _offscreenFB;

        // buffers
        private DeviceBuffer _mvpBuffer;

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


            var vertexShaderDesc = new ShaderDescription(
                ShaderStages.Vertex,
                Encoding.UTF8.GetBytes(VertexCode),
                "main");
            var fragmentShaderDesc = new ShaderDescription(
                ShaderStages.Fragment,
                Encoding.UTF8.GetBytes(FragmentCode),
                "main");

            _shaders = factory.CreateFromSpirv(vertexShaderDesc, fragmentShaderDesc);


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

            _mvpBuffer = factory.CreateBuffer(new BufferDescription((uint)sizeof(ModelViewProjection), BufferUsage.UniformBuffer));

            _commandList.Begin();
            Clear();
        }

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool IsDisposed { get; private set; }

        public Camera Camera { get; set; }

        public Light? Light { get; set; }

        public void Clear()
        {
            _commandList.SetFramebuffer(_offscreenFB);
            _commandList.ClearColorTarget(0, RgbaFloat.Black);
            _commandList.ClearDepthStencil(1f);
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

                _mvpBuffer.Dispose();

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

        public unsafe void DrawTexture(Texture texture)
        {
            if (texture.PlatformImpl is TextureImpl impl)
            {
                var factory = _graphicsDevice.ResourceFactory;
                var tex = ToTexture(impl);
                var view = factory.CreateTextureView(tex);

                // VertexBuffer
                var vertexBuffer = factory.CreateBuffer(new BufferDescription((uint)(sizeof(VertexPositionTexture) * 4), BufferUsage.VertexBuffer));
                _graphicsDevice.UpdateBuffer(vertexBuffer, 0, impl.Vertices.ToArray());

                // IndexBuffer
                uint[] indices =
                {
                    0, 1, 3,
                    1, 2, 3,
                };
                var indexBuffer = factory.CreateBuffer(new BufferDescription(sizeof(uint) * (uint)indices.Length, BufferUsage.IndexBuffer));
                _graphicsDevice.UpdateBuffer(indexBuffer, 0, indices);

                var shaderSet = new ShaderSetDescription(
                    new[]
                    {
                        new VertexLayoutDescription(
                            new VertexElementDescription("aPosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float3),
                            new VertexElementDescription("aTexCoord", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2))
                    },
                    _textureShader);

                var layout = factory.CreateResourceLayout(new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("MVP", ResourceKind.UniformBuffer, ShaderStages.Vertex),
                new ResourceLayoutElementDescription("Texture0", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
                new ResourceLayoutElementDescription("Sampler", ResourceKind.Sampler, ShaderStages.Fragment),
                    new ResourceLayoutElementDescription("ColorBuffer", ResourceKind.UniformBuffer, ShaderStages.Fragment)));

                // MVP
                UpdateBuffer(Camera, texture.Transform);

                // ColorBuffer
                var colorBuffer = factory.CreateBuffer(new BufferDescription((uint)sizeof(Vector4), BufferUsage.UniformBuffer));
                _graphicsDevice.UpdateBuffer(
                    colorBuffer, 0,
                    texture.Color.ToFloat());

                var resourceSet = factory.CreateResourceSet(
                    new ResourceSetDescription(layout, _mvpBuffer, view, _graphicsDevice.PointSampler, colorBuffer));

                var pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription(
                    BlendStateDescription.SingleOverrideBlend,
                    new DepthStencilStateDescription(
                        true,
                        true,
                        ComparisonKind.LessEqual),
                    new RasterizerStateDescription(
                        FaceCullMode.Back,
                        PolygonFillMode.Solid,
                        FrontFace.Clockwise,
                        true,
                        false),
                    PrimitiveTopology.TriangleStrip,
                    shaderSet,
                    layout,
                    _offscreenFB.OutputDescription));

                _commandList.SetVertexBuffer(0, vertexBuffer);
                _commandList.SetIndexBuffer(indexBuffer, IndexFormat.UInt32);
                _commandList.SetPipeline(pipeline);
                _commandList.SetGraphicsResourceSet(0, resourceSet);
                _commandList.Draw(4, 1, 0, 0);
                //var (pipeline, vertex, index) = CreatePipeline();
                //_commandList.SetVertexBuffer(0, vertex);
                //_commandList.SetIndexBuffer(index, IndexFormat.UInt16);
                //_commandList.SetPipeline(pipeline);
                //_commandList.DrawIndexed(
                //    indexCount: 4,
                //    instanceCount: 1,
                //    indexStart: 0,
                //    vertexOffset: 0,
                //    instanceStart: 0);

                //index.Dispose();
                //vertex.Dispose();
                //pipeline.Dispose();
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

            _graphicsDevice.WaitForIdle();
            //_graphicsDevice.SwapBuffers();

            var buf = _graphicsDevice.Map(_stage, MapMode.Read);
            fixed (BGRA32* dst = image.Data)
            {
                var size = image.DataSize;
                Buffer.MemoryCopy((void*)buf.Data, dst, size, size);
            }

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

        struct VertexPositionColor
        {
            public Vector2 Position; // This is the position, in normalized device coordinates.
            public RgbaFloat Color; // This is the color of the vertex.
            public VertexPositionColor(Vector2 position, RgbaFloat color)
            {
                Position = position;
                Color = color;
            }
            public const uint SizeInBytes = 24;
        }

        private (Pipeline pipeline, DeviceBuffer vertex, DeviceBuffer index) CreatePipeline()
        {
            var factory = _graphicsDevice.ResourceFactory;

            VertexPositionColor[] quadVertices =
            {
                new VertexPositionColor(new Vector2(-0.75f, 0.75f), RgbaFloat.Red),
                new VertexPositionColor(new Vector2(0.75f, 0.75f), RgbaFloat.Green),
                new VertexPositionColor(new Vector2(-0.75f, -0.75f), RgbaFloat.Blue),
                new VertexPositionColor(new Vector2(0.75f, -0.75f), RgbaFloat.Yellow)
            };

            ushort[] quadIndices = { 0, 1, 2, 3 };

            var vertexBuffer = factory.CreateBuffer(new BufferDescription(4 * VertexPositionColor.SizeInBytes, BufferUsage.VertexBuffer));
            var indexBuffer = factory.CreateBuffer(new BufferDescription(4 * sizeof(ushort), BufferUsage.IndexBuffer));

            _graphicsDevice.UpdateBuffer(vertexBuffer, 0, quadVertices);
            _graphicsDevice.UpdateBuffer(indexBuffer, 0, quadIndices);

            var pipelineDescription = new GraphicsPipelineDescription();
            pipelineDescription.BlendState = BlendStateDescription.SingleOverrideBlend;

            pipelineDescription.DepthStencilState = new DepthStencilStateDescription(
                true,
                true,
                comparisonKind: ComparisonKind.LessEqual);

            pipelineDescription.RasterizerState = new RasterizerStateDescription(
                cullMode: FaceCullMode.Back,
                fillMode: PolygonFillMode.Solid,
                frontFace: FrontFace.Clockwise,
                true,
                false);

            pipelineDescription.PrimitiveTopology = PrimitiveTopology.TriangleStrip;
            pipelineDescription.ResourceLayouts = System.Array.Empty<ResourceLayout>();

            var vertexLayout = new VertexLayoutDescription(
                new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
                new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4));

            pipelineDescription.ShaderSet = new ShaderSetDescription(
                vertexLayouts: new VertexLayoutDescription[] { vertexLayout },
                shaders: _shaders);

            pipelineDescription.Outputs = _offscreenFB.OutputDescription;
            return (_graphicsDevice.ResourceFactory.CreateGraphicsPipeline(pipelineDescription), vertexBuffer, indexBuffer);
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
            _graphicsDevice.UpdateBuffer(
                _mvpBuffer,
                0,
                new ModelViewProjection(transform.Matrix, camera.GetViewMatrix(), camera.GetProjectionMatrix()));
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

    [StructLayout(LayoutKind.Sequential)]
    public struct ModelViewProjection
    {
        public Matrix4x4 Model;
        public Matrix4x4 View;
        public Matrix4x4 Projection;

        public ModelViewProjection(Matrix4x4 model, Matrix4x4 view, Matrix4x4 projection)
        {
            Model = model;
            View = view;
            Projection = projection;
        }
    }
}
