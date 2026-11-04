using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.Models;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

public partial class ShaderNodeRenderingTests
{
    private const string CornerVertexCode = """
        #version 450
        void main() {
            vec2 p=vec2((gl_VertexIndex<<1)&2,gl_VertexIndex&2);
            gl_Position=vec4(p*2.0-1.0,0,1);
        }
        """;

    private const string FlatFragmentCode = """
        #version 450
        layout(location=0) out vec4 color;
        void main() { color=vec4(0.25,0.5,0.75,1); }
        """;

    private static IOutputPort FilledBuffer(GraphModel graph, ShaderDataType type, string text, int count, int stride)
    {
        var data = graph.Add(new ShaderDataNode());
        data.DataType.Property!.SetValue(type);
        data.Text.Property!.SetValue(text);
        var buffer = graph.Add(new ShaderBufferNode());
        buffer.Count.Property!.SetValue(count);
        buffer.Stride.Property!.SetValue(stride);
        graph.Connect(buffer.Data, data.Output);
        return buffer.Output;
    }

    // Vertices 3 to 5 make another triangle than 0 to 2 do, so each case also shows that its offset arrived.
    [TestCase(ShaderDrawMode.Vertices)]
    [TestCase(ShaderDrawMode.Indexed)]
    [TestCase(ShaderDrawMode.Indirect)]
    public void EachDrawModeDrawsLikeTheBuilder(ShaderDrawMode mode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var pass = graph.Add(new ShaderRenderPassNode { Vertex = { Value = CornerVertexCode }, Fragment = { Value = FlatFragmentCode } });
            Assert.That(pass.Error.Value, Is.Null);
            var draw = graph.Add(new ShaderDrawNode { Mode = { Value = mode } });
            graph.Connect(pass.Settings, draw.Output);
            switch (mode)
            {
                case ShaderDrawMode.Vertices:
                    draw.NamedInput("FirstVertex").Property!.SetValue(3);
                    break;
                case ShaderDrawMode.Indexed:
                    draw.NamedInput("VertexOffset").Property!.SetValue(3);
                    var indices = graph.Add(new ShaderIndexBufferNode());
                    indices.Format.Property!.SetValue(IndexFormat.UInt16);
                    graph.Connect(indices.Buffer, FilledBuffer(graph, ShaderDataType.HexBytes, "0000 0100 0200", 3, 2));
                    graph.Connect(pass.Settings, indices.Output);
                    break;
                case ShaderDrawMode.Indirect:
                    draw.NamedInput("Offset").Property!.SetValue(16);
                    graph.Connect(draw.NamedInput("Commands"), FilledBuffer(graph, ShaderDataType.UInt32, "0 0 0 0 3 1 3 0", 8, 4));
                    break;
            }

            return pass.Out("color");
        });

        AssertMatches(graph, ShaderGraphBuilder.Build(mode, static (builder, mode) =>
        {
            ShaderImage image = builder.Image2D("output", builder.Source);
            ShaderBuffer? extra = mode switch
            {
                ShaderDrawMode.Indexed => builder.Buffer<ushort>("indices", new ushort[] { 0, 1, 2 }),
                ShaderDrawMode.Indirect => builder.Buffer<uint>("commands", new uint[] { 0, 0, 0, 0, 3, 1, 3, 0 }),
                _ => null
            };
            builder.Render("draw", ShaderProgram.Graphics(new(CornerVertexCode), new(FlatFragmentCode)), pass =>
            {
                pass.ColorAttachment(0, image);
                switch (mode)
                {
                    case ShaderDrawMode.Vertices:
                        pass.Draw(3, 1, 3, 0);
                        break;
                    case ShaderDrawMode.Indexed:
                        pass.IndexBuffer(extra!, IndexFormat.UInt16);
                        pass.DrawIndexed(3, vertexOffset: 3);
                        break;
                    case ShaderDrawMode.Indirect:
                        pass.DrawIndirect(extra!, offset: 16);
                        break;
                }
            });
            return builder.Output(image);
        }));
    }

    private const string FillCode = """
        #version 450
        layout(local_size_x=8, local_size_y=8) in;
        layout(set=0,binding=0,rgba16f) uniform writeonly image2D color;
        void main() {
            ivec2 p=ivec2(gl_GlobalInvocationID.xy);
            if(any(greaterThanEqual(p,imageSize(color)))) return;
            imageStore(color,p,vec4(0.25,0.5,0.75,1));
        }
        """;

    // A dispatch can leave part of the image unwritten, so both sides clear it first.
    [TestCase(ShaderDispatchMode.Image)]
    [TestCase(ShaderDispatchMode.Invocations)]
    public void EachDispatchModeRunsLikeTheBuilder(ShaderDispatchMode mode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var target = graph.Add(new ShaderImageNode());
            var clear = graph.Add(new ShaderClearImageNode());
            graph.Connect(clear.Image, target.Output);
            var pass = graph.Add(new ShaderComputePassNode { Code = { Value = FillCode } });
            Assert.That(pass.Error.Value, Is.Null);
            graph.Connect(pass.In("color"), clear.Output);
            var dispatch = graph.Add(new ShaderDispatchNode { Mode = { Value = mode } });
            graph.Connect(pass.Settings, dispatch.Output);
            if (mode == ShaderDispatchMode.Image)
            {
                graph.Connect(dispatch.NamedInput("Image"), target.Output);
            }
            else
            {
                dispatch.NamedInput("X").Property!.SetValue(12);
                dispatch.NamedInput("Y").Property!.SetValue(4);
            }

            return pass.Out("color");
        });

        AssertMatches(graph, ShaderGraphBuilder.Build(mode, static (builder, mode) =>
        {
            ShaderImage image = builder.Image2D("image", builder.Source);
            builder.Clear("clear", image);
            builder.Compute("fill", ShaderProgram.Compute(new(FillCode)), pass =>
            {
                pass.StorageImage(0, image, ResourceAccess.Write);
                if (mode == ShaderDispatchMode.Image) pass.DispatchImage(image);
                else pass.DispatchThreads(12, 4);
            });
            return builder.Output(image);
        }));
    }

    private const string ScalarCode = """
        #version 450
        layout(location=0) out vec4 color;
        layout(constant_id=0) const float gain=0.25;
        layout(constant_id=1) const bool bright=false;
        layout(constant_id=2) const uint count=1u;
        layout(set=0,binding=0,std140) uniform Parameters { bool flag; uint steps; int offset; } parameters;
        void main() {
            color=vec4(gain,(bright?0.75:0.5)*(parameters.flag?1.0:0.5),
                float(count)/8.0+float(parameters.steps)/16.0,float(parameters.offset+2)/4.0);
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public void ConstantsAndBlockMembersOfEachTypeReachTheCode(bool changeTheConstants)
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var pass = graph.Add(new ShaderFullscreenPassNode { Code = { Value = ScalarCode } });
            Assert.That(pass.Error.Value, Is.Null);
            // SPIR-V keeps a bool in a block as a number, so the port cannot know it was one.
            Assert.That(pass.In("parameters.flag").AssociatedType, Is.EqualTo(typeof(uint)));
            pass.Set("parameters.flag", 1u);
            pass.Set("parameters.steps", 3u);
            pass.Set("parameters.offset", 1);
            if (changeTheConstants)
            {
                pass.Set("gain", 0.75f);
                pass.Set("bright", true);
                pass.Set("count", 4u);
            }

            return pass.Out("color");
        });

        AssertMatches(graph, ShaderGraphBuilder.Build(changeTheConstants, static (builder, change) =>
        {
            ShaderImage image = builder.Image2D("result", builder.Source);
            byte[] data = new byte[16];
            BitConverter.GetBytes(1).CopyTo(data, 0);
            BitConverter.GetBytes(3u).CopyTo(data, 4);
            BitConverter.GetBytes(1).CopyTo(data, 8);
            ShaderBuffer buffer = builder.Buffer("parameters", data, 1);
            ShaderProgram program = ShaderProgram.Fullscreen(new ShaderSource(ScalarCode));
            if (change)
            {
                program = program.Specialize(
                [
                    SpecializationConstant.Create(0, 0.75f, ShaderStage.Fragment),
                    SpecializationConstant.Create(1, true, ShaderStage.Fragment),
                    SpecializationConstant.Create(2, 4u, ShaderStage.Fragment)
                ]);
            }

            builder.Fullscreen("main", program, pass =>
            {
                pass.UniformBuffer(0, buffer);
                pass.ColorAttachment(0, image);
            });
            return builder.Output(image);
        }));
    }

    private const string ReadOnlyImageCode = """
        #version 450
        layout(local_size_x=8, local_size_y=8) in;
        layout(set=0,binding=0,rgba16f) uniform readonly image2D source;
        layout(set=0,binding=1,rgba16f) uniform writeonly image2D color;
        void main() {
            ivec2 p=ivec2(gl_GlobalInvocationID.xy);
            if(any(greaterThanEqual(p,imageSize(color)))) return;
            imageStore(color,p,imageLoad(source,p)*vec4(0.5,0.25,0.75,1));
        }
        """;

    [Test]
    public void AReadOnlyImageWithNothingConnectedReadsTheSource()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var pass = graph.Add(new ShaderComputePassNode { Code = { Value = ReadOnlyImageCode } });
            Assert.That(pass.Error.Value, Is.Null);
            return pass.Out("color");
        });

        AssertMatches(graph, ShaderGraphBuilder.Build(0, static (builder, _) =>
        {
            ShaderImage image = builder.Image2D("color", builder.Source);
            builder.Compute("main", ShaderProgram.Compute(new(ReadOnlyImageCode)), pass =>
            {
                pass.StorageImage(0, builder.Source, ResourceAccess.Read);
                pass.StorageImage(1, image, ResourceAccess.Write);
                pass.DispatchImage(image);
            });
            return builder.Output(image);
        }));
    }

    private const string ReadBufferCode = """
        #version 450
        layout(location=0) out vec4 color;
        layout(set=0,binding=0,std430) readonly buffer Data { uint values[]; } data;
        void main() { color=vec4(float(data.values[1])/8.0,0,0,1); }
        """;

    [Test]
    public void ClearingABufferFillsItWithTheValue()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var buffer = graph.Add(new ShaderBufferNode());
            buffer.Count.Property!.SetValue(2);
            buffer.Stride.Property!.SetValue(4);
            var clear = graph.Add(new ShaderClearBufferNode());
            clear.Value.Property!.SetValue(3u);
            graph.Connect(clear.Buffer, buffer.Output);
            var pass = graph.Add(new ShaderFullscreenPassNode { Code = { Value = ReadBufferCode } });
            Assert.That(pass.Error.Value, Is.Null);
            graph.Connect(pass.In("data"), clear.Output);
            return pass.Out("color");
        });

        ShaderGraphDescription reference = ShaderGraphBuilder.Build(0, static (builder, _) =>
        {
            ShaderImage image = builder.Image2D("color", builder.Source);
            ShaderBuffer buffer = builder.Buffer<uint>("data", 2);
            builder.Clear("clear", buffer, 3);
            builder.Fullscreen("main", ShaderProgram.Fullscreen(new ShaderSource(ReadBufferCode)), pass =>
            {
                pass.StorageBuffer(0, buffer, ResourceAccess.Read);
                pass.ColorAttachment(0, image);
            });
            return builder.Output(image);
        });
        Assert.That(Channel(ShaderNodeTestHelpers.Render(reference, 1), 0), Is.EqualTo(0.375f).Within(1e-3f));
        AssertMatches(graph, reference);
    }

    [Test]
    public void ClearingADepthImageSetsWhatAPassThenReads()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var depth = graph.Add(new ShaderFixedImageNode());
            depth.Width.Property!.SetValue(32);
            depth.Height.Property!.SetValue(24);
            depth.Format.Property!.SetValue(TextureFormat.Depth32Float);
            var clear = graph.Add(new ShaderClearImageNode());
            clear.Depth.Property!.SetValue(0.25f);
            graph.Connect(clear.Image, depth.Output);
            var nearest = graph.Add(new ShaderSamplerNode());
            nearest.MinFilter.Property!.SetValue(SamplerFilter.Nearest);
            nearest.MagFilter.Property!.SetValue(SamplerFilter.Nearest);
            nearest.MipmapFilter.Property!.SetValue(SamplerFilter.Nearest);
            graph.Connect(nearest.Image, clear.Output);
            var view = graph.Add(new ShaderFullscreenPassNode { Code = { Value = DepthViewCode } });
            graph.Connect(view.In("depth"), nearest.Output);
            return view.Out("color");
        });

        ushort[] pixels = ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(graph), 1);

        Assert.That(Channel(pixels, 0), Is.EqualTo(0.25f).Within(1e-3f));
    }

    private static IOutputPort Rectangle(GraphModel graph, float width, float height)
    {
        var geometry = graph.Add(new RectGeometryNode());
        ((IInputPort)geometry.Items.Single(item => item.Name.Value == "Width")).Property!.SetValue(width);
        ((IInputPort)geometry.Items.Single(item => item.Name.Value == "Height")).Property!.SetValue(height);
        var shape = graph.Add(new GeometryShapeNode());
        shape.Fill.Property!.SetValue(new SolidColorBrush(Colors.White));
        graph.Connect(shape.Geometry, geometry.OutputPort);
        return shape.Output;
    }

    [Test]
    public void APictureOfNothingOrOfSeveralPiecesLeavesTheSourceWhereItIs()
    {
        VulkanTestEnvironment.EnsureAvailable();
        const string cutOut = """
            #version 450
            layout(location=0) in vec2 uv;
            layout(location=0) out vec4 color;
            layout(set=0,binding=0) uniform sampler2D source;
            layout(set=0,binding=1) uniform sampler2D matte;
            void main() { color=texture(source,uv)*(1.0-texture(matte,uv).a); }
            """;
        using var files = new SceneHistoryHarness("shader-picture-pieces", duration: MediaTime.FromSeconds(1));
        var effect = new NodeGraphFilterEffect();
        GraphModel graph = effect.Model.Value!;
        var input = graph.Add(new FilterEffectInputNode());
        var pieces = graph.Add(new TransformNode());
        var pass = graph.Add(new ShaderFullscreenPassNode { Code = { Value = cutOut } });
        graph.Connect(pass.In("source"), input.Output);
        graph.Connect(pass.In("matte"), pieces.Items.OfType<IOutputPort>().Single());
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        Populate(files, effect);
        using var renderer = new SceneRenderer(files.Scene, RenderIntent.Preview, 1);
        // The rectangle is centered in the frame, and an effect works in its coordinates.
        float Alpha(ushort[] pixels, int x, int y) => Channel(pixels, ((y + 12) * 48 + x + 12) * 4 + 3);

        ushort[] nothing = RenderScene(renderer, MediaTime.Zero, 1);

        Assert.That(pass.Error.Value, Is.Null);
        Assert.That(Alpha(nothing, 2, 10), Is.EqualTo(1f).Within(1e-3f));
        Assert.That(Alpha(nothing, 23, 15), Is.EqualTo(1f).Within(1e-3f));
        Assert.That(Alpha(nothing, -1, 10), Is.Zero.Within(1e-3f));
        Assert.That(Alpha(nothing, 24, 10), Is.Zero.Within(1e-3f));

        var list = pieces.Items.OfType<ListInputPort<RenderNode?>>().Single();
        graph.Connect(list, Rectangle(graph, 6, 1000));
        graph.Connect(list, Rectangle(graph, 1000, 4));
        ushort[] both = RenderScene(renderer, MediaTime.Zero, 1);

        Assert.That(pass.Error.Value, Is.Null);
        Assert.That(Alpha(both, 2, 10), Is.Zero.Within(1e-3f), "Under the first piece");
        Assert.That(Alpha(both, 12, 2), Is.Zero.Within(1e-3f), "Under the second piece");
        Assert.That(Alpha(both, 12, 10), Is.EqualTo(1f).Within(1e-3f), "Under neither");
    }
}
