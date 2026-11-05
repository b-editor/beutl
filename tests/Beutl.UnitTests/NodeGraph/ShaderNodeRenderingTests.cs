using System.Numerics;
using System.Reactive.Subjects;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Models;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

/// <summary>Each test authors one effect with nodes and the same effect through the builder API, and compares the pixels.</summary>
[NonParallelizable]
public partial class ShaderNodeRenderingTests
{
    private static GraphModel Restored(GraphModel graph)
    {
        var restored = new GraphModel();
        CoreSerializer.PopulateFromJsonObject(restored, CoreSerializer.SerializeToJsonObject(graph));
        return restored;
    }

    private static void AssertMatches(GraphModel graph, ShaderGraphDescription reference, float scale = 1)
    {
        ushort[] expected = ShaderNodeTestHelpers.Render(reference, scale);
        Assert.That(expected, Has.Some.Not.EqualTo((ushort)0));
        Assert.That(ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(graph), scale), Is.EqualTo(expected), "As authored");
        Assert.That(ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(Restored(graph)), scale), Is.EqualTo(expected), "After saving");
    }

    private const string ComputeCode = """
        #version 450
        layout(local_size_x=8, local_size_y=8) in;
        layout(set=0,binding=0,rgba16f) uniform writeonly image2D maskImage;
        layout(set=0,binding=1,std140) uniform Parameters { float amount; } parameters;
        void main() {
            ivec2 p=ivec2(gl_GlobalInvocationID.xy);
            ivec2 size=imageSize(maskImage);
            if(any(greaterThanEqual(p,size))) return;
            imageStore(maskImage,p,vec4(parameters.amount*float(p.x+1)/float(size.x),0.5,0.25,1));
        }
        """;

    private const string CombineCode = """
        #version 450
        layout(location=0) in vec2 uv;
        layout(location=0) out vec4 color;
        layout(location=1) out vec4 second;
        layout(set=0,binding=0) uniform sampler2D sourceImage;
        layout(set=0,binding=1) uniform sampler2D maskImage;
        void main() {
            vec4 source=texture(sourceImage,uv);
            vec4 mask=texture(maskImage,uv);
            color=vec4(source.rgb*mask.rgb,source.a);
            second=mask;
        }
        """;

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void ComputeThenAPassWithTwoInputsAndOutputs(float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        var graph = new GraphModel();
        var mask = graph.Add(new ShaderImageNode());
        mask.ResolutionScale.Property!.SetValue(0.5f);
        var compute = graph.Add(new ShaderComputePassNode { Code = { Value = ComputeCode } });
        compute.Set("parameters.amount", 0.75f);
        graph.Connect(compute.In("maskImage"), mask.Output);
        var combine = graph.Add(new ShaderFullscreenPassNode { Code = { Value = CombineCode } });
        graph.Connect(combine.In("maskImage"), compute.Out("maskImage"));
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, combine.Out("color"));

        AssertMatches(graph, ShaderGraphBuilder.Build(0, static (builder, _) =>
        {
            ShaderImage mask = builder.Image2D("mask", builder.Source, resolutionScale: 0.5f);
            ShaderBuffer parameters = builder.Buffer<float>("parameters", new[] { 0.75f, 0, 0, 0 });
            builder.Compute("compute", ShaderProgram.Compute(new ShaderSource(ComputeCode)), pass =>
            {
                pass.StorageImage(0, mask, ResourceAccess.Write);
                pass.UniformBuffer(1, parameters);
                pass.DispatchImage(mask);
            });
            ShaderImage output = builder.Image2D("output", builder.Source);
            ShaderImage second = builder.Image2D("second", builder.Source);
            output.RasterSource = builder.Source;
            second.RasterSource = builder.Source;
            builder.Fullscreen("combine", ShaderProgram.Fullscreen(new ShaderSource(CombineCode)), pass =>
            {
                pass.SampledImage(0, builder.Source);
                pass.SampledImage(1, mask);
                pass.ColorAttachment(0, output);
                pass.ColorAttachment(1, second);
            });
            return builder.Output(output);
        }), scale);
    }

    [Test]
    public void SpecializationConstantsSizeTheBlocksTheyAppearIn()
    {
        VulkanTestEnvironment.EnsureAvailable();
        const string code = """
            #version 450
            layout(location=0) out vec4 color;
            layout(constant_id=3) const int count=2;
            layout(set=0,binding=1,std140) uniform Parameters {
                layout(offset=0) float gain;
                layout(offset=16) vec3 tint;
                layout(offset=32) mat2 basis;
                layout(offset=64) float weights[count];
            } parameters;
            layout(push_constant) uniform Constants { vec4 bias; } constants;
            void main() {
                color=vec4(parameters.tint*parameters.gain*parameters.weights[count-1]*parameters.basis[1][1],1)+constants.bias;
            }
            """;
        byte[] basis = new byte[32];
        BitConverter.GetBytes(0.5f).CopyTo(basis, 20);
        byte[] weights = new byte[64];
        BitConverter.GetBytes(0.75f).CopyTo(weights, 48);
        var graph = new GraphModel();
        var pass = graph.Add(new ShaderFullscreenPassNode { Code = { Value = code } });
        Assert.That(pass.Error.Value, Is.Null);
        pass.Set("count", 4);
        pass.Set("parameters.gain", 0.8f);
        pass.Set("parameters.tint", new Vector3(1, 0.5f, 0.25f));
        pass.Set("parameters.basis", basis);
        pass.Set("parameters.weights", weights);
        pass.Set("constants.bias", new Vector4(0.1f, 0, 0, 0));
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));

        AssertMatches(graph, ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            ShaderImage image = builder.Image2D("result", builder.Source);
            byte[] data = new byte[128];
            BitConverter.GetBytes(0.8f).CopyTo(data, 0);
            BitConverter.GetBytes(1f).CopyTo(data, 16);
            BitConverter.GetBytes(0.5f).CopyTo(data, 20);
            BitConverter.GetBytes(0.25f).CopyTo(data, 24);
            basis.CopyTo(data, 32);
            weights.CopyTo(data, 64);
            ShaderBuffer buffer = builder.Buffer("parameters", data, 1);
            builder.Fullscreen("main", ShaderProgram.Fullscreen(new ShaderSource(code))
                .Specialize([SpecializationConstant.Create(3, 4, ShaderStage.Fragment)]), pass =>
            {
                pass.UniformBuffer(1, buffer);
                pass.PushConstants(new Vector4(0.1f, 0, 0, 0));
                pass.ColorAttachment(0, image);
            });
            return builder.Output(image);
        }));
    }

    [Test]
    public void AMatrixPortMapsPointsAsTheMatrixDoes()
    {
        VulkanTestEnvironment.EnsureAvailable();
        const string code = """
            #version 450
            layout(location=0) out vec4 color;
            layout(set=0,binding=0) uniform Parameters { mat3 flat_; mat4 full; } parameters;
            void main() {
                vec3 a=parameters.flat_*vec3(1,2,1);
                vec4 b=parameters.full*vec4(1,2,0,1);
                color=vec4(a.x,a.y,b.x,b.y)/32.0;
            }
            """;
        Matrix matrix = Matrix.CreateScale(2, 3) * Matrix.CreateTranslation(5, 7);
        Point expected = new Point(1, 2) * matrix;
        var graph = new GraphModel();
        var pass = graph.Add(new ShaderFullscreenPassNode { Code = { Value = code } });
        pass.Set("parameters.flat_", matrix);
        pass.Set("parameters.full", matrix);
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));

        ushort[] pixels = ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(graph), 1);

        float[] values = [.. pixels.Take(4).Select(texel => (float)BitConverter.UInt16BitsToHalf(texel))];
        Assert.That(values, Is.EqualTo(new[] { expected.X / 32, expected.Y / 32, expected.X / 32, expected.Y / 32 }).Within(1e-3f));
    }

    private const string GenerateCode = """
        #version 450
        layout(local_size_x=1) in;
        layout(binding=0,std430) writeonly buffer Commands { uint words[]; } commands;
        void main() {
            for(int i=0;i<9;i++) commands.words[i]=0;
            commands.words[0]=1; commands.words[1]=1; commands.words[2]=1;
            commands.words[4]=3; commands.words[5]=1;
        }
        """;

    private const string ColorCode = """
        #version 450
        layout(local_size_x=1) in;
        layout(binding=0,std430) writeonly buffer Color { vec4 value; } color;
        void main() { color.value=vec4(.25,.5,.75,1); }
        """;

    private const string VertexCode = """
        #version 450
        layout(location=0) in vec2 position;
        void main() { gl_Position=vec4(position,.5,1); }
        """;

    private const string FragmentCode = """
        #version 450
        layout(binding=0,std430) readonly buffer Color { vec4 value; } color;
        layout(location=0) out vec4 result;
        void main() { result=color.value; }
        """;

    [Test]
    public void ComputeWrittenCommandsDriveAnIndirectDispatchAndAnIndexedDraw()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var graph = new GraphModel();
        ShaderBufferNode Buffer(int count, int stride)
        {
            var node = graph.Add(new ShaderBufferNode());
            node.Count.Property!.SetValue(count);
            node.Stride.Property!.SetValue(stride);
            return node;
        }

        var generated = Buffer(9, 4);
        var once = graph.Add(new ShaderDispatchNode { Mode = { Value = ShaderDispatchMode.Workgroups } });
        var generate = graph.Add(new ShaderComputePassNode { Code = { Value = GenerateCode } });
        graph.Connect(generate.In("commands"), generated.Output);
        graph.Connect(generate.Settings, once.Output);
        var copy = graph.Add(new ShaderCopyBufferNode());
        graph.Connect(copy.Source, generate.Out("commands"));
        graph.Connect(copy.Destination, Buffer(9, 4).Output);

        var indirect = graph.Add(new ShaderDispatchNode { Mode = { Value = ShaderDispatchMode.Indirect } });
        graph.Connect(indirect.NamedInput("Commands"), copy.Output);
        var color = graph.Add(new ShaderComputePassNode { Code = { Value = ColorCode } });
        graph.Connect(color.In("color"), Buffer(1, 16).Output);
        graph.Connect(color.Settings, indirect.Output);

        var vertexData = graph.Add(new ShaderDataNode());
        vertexData.Text.Property!.SetValue("-1 -1  3 -1  -1 3");
        var vertices = Buffer(3, 8);
        graph.Connect(vertices.Data, vertexData.Output);
        var attribute = graph.Add(new ShaderVertexAttributeNode());
        attribute.Format.Property!.SetValue(VertexFormat.Float2);
        var vertexBuffer = graph.Add(new ShaderVertexBufferNode());
        vertexBuffer.Stride.Property!.SetValue(8);
        graph.Connect(vertexBuffer.Buffer, vertices.Output);
        graph.Connect(vertexBuffer.Attributes, attribute.Output);

        var indexData = graph.Add(new ShaderDataNode());
        indexData.DataType.Property!.SetValue(ShaderDataType.HexBytes);
        indexData.Text.Property!.SetValue("0000 0100 0200");
        var indices = Buffer(3, 2);
        graph.Connect(indices.Data, indexData.Output);
        var indexBuffer = graph.Add(new ShaderIndexBufferNode());
        indexBuffer.Format.Property!.SetValue(IndexFormat.UInt16);
        graph.Connect(indexBuffer.Buffer, indices.Output);

        var draw = graph.Add(new ShaderDrawNode { Mode = { Value = ShaderDrawMode.IndexedIndirect } });
        draw.NamedInput("Offset").Property!.SetValue(16);
        graph.Connect(draw.NamedInput("Commands"), copy.Output);

        var render = graph.Add(new ShaderRenderPassNode { Vertex = { Value = VertexCode }, Fragment = { Value = FragmentCode } });
        Assert.That(render.Error.Value, Is.Null);
        graph.Connect(render.In("color"), color.Out("color"));
        foreach (IOutputPort setting in new IOutputPort[] { vertexBuffer.Output, indexBuffer.Output, draw.Output })
            graph.Connect(render.Settings, setting);
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, render.Out("result"));

        ShaderGraphDescription reference = ShaderGraphBuilder.Build(0, static (builder, _) =>
        {
            ShaderBuffer generatedBuffer = builder.Buffer<uint>("generated", 9);
            ShaderBuffer commandBuffer = builder.Buffer<uint>("commands", 9);
            ShaderBuffer colorBuffer = builder.Buffer<Vector4>("color", 1);
            builder.Compute("generate", ShaderProgram.Compute(new(GenerateCode)), pass =>
            {
                pass.StorageBuffer(0, generatedBuffer, ResourceAccess.Write);
                pass.Dispatch(1);
            });
            builder.Copy("copy", generatedBuffer, commandBuffer);
            builder.Compute("color", ShaderProgram.Compute(new(ColorCode)), pass =>
            {
                pass.StorageBuffer(0, colorBuffer, ResourceAccess.Write);
                pass.DispatchIndirect(commandBuffer);
            });
            ShaderImage image = builder.Image2D("output", builder.Source);
            ShaderBuffer vertexStorage = builder.Buffer<Vector2>("vertices", new Vector2[] { new(-1, -1), new(3, -1), new(-1, 3) });
            ShaderBuffer indexStorage = builder.Buffer<ushort>("indices", new ushort[] { 0, 1, 2 });
            builder.Render("draw", ShaderProgram.Graphics(new(VertexCode), new(FragmentCode)), pass =>
            {
                pass.StorageBuffer(0, colorBuffer, ResourceAccess.Read);
                pass.ColorAttachment(0, image);
                pass.VertexInput(new() { Bindings = [new() { Stride = 8 }], Attributes = [new() { Format = VertexFormat.Float2 }] });
                pass.VertexBuffer(0, vertexStorage);
                pass.IndexBuffer(indexStorage, IndexFormat.UInt16);
                pass.DrawIndexedIndirect(commandBuffer, offset: 16);
            });
            return builder.Output(image);
        });
        Assert.That(ShaderNodeTestHelpers.Render(reference, 1)[0], Is.EqualTo(BitConverter.HalfToUInt16Bits((Half)0.25f)));
        AssertMatches(graph, reference);
    }

    private const string DepthVertexCode = """
        #version 450
        layout(push_constant) uniform Constants { float depth; float size; } constants;
        void main() {
            vec2 p=vec2((gl_VertexIndex<<1)&2,gl_VertexIndex&2);
            gl_Position=vec4((p*2.0-1.0)*constants.size,constants.depth,1);
        }
        """;

    private const string DepthFragmentCode = """
        #version 450
        layout(location=0) out vec4 color;
        layout(push_constant) uniform Constants { float depth; float size; } constants;
        void main() { color=vec4(constants.depth,constants.size,0,1); }
        """;

    private const string DepthViewCode = """
        #version 450
        layout(location=0) in vec2 uv;
        layout(location=0) out vec4 color;
        layout(set=0,binding=0) uniform sampler2D depth;
        void main() { color=vec4(texture(depth,uv).r); }
        """;

    [Test]
    public void ADepthImageCanBeReadAfterThePassThatWroteIt()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var graph = new GraphModel();
        var depthImage = graph.Add(new ShaderFixedImageNode());
        depthImage.Width.Property!.SetValue(32);
        depthImage.Height.Property!.SetValue(24);
        depthImage.Format.Property!.SetValue(TextureFormat.Depth32Float);
        var colorImage = graph.Add(new ShaderFixedImageNode());
        colorImage.Width.Property!.SetValue(32);
        colorImage.Height.Property!.SetValue(24);
        var depth = graph.Add(new ShaderDepthNode());
        graph.Connect(depth.Image, depthImage.Output);
        var pass = graph.Add(new ShaderRenderPassNode { Vertex = { Value = DepthVertexCode }, Fragment = { Value = DepthFragmentCode } });
        Assert.That(pass.Error.Value, Is.Null);
        pass.Set("constants.depth", 0.25f);
        pass.Set("constants.size", 1f);
        graph.Connect(pass.Targets, colorImage.Output);
        graph.Connect(pass.Settings, depth.Output);
        var view = graph.Add(new ShaderFullscreenPassNode { Code = { Value = DepthViewCode } });
        var nearest = graph.Add(new ShaderSamplerNode());
        nearest.MinFilter.Property!.SetValue(SamplerFilter.Nearest);
        nearest.MagFilter.Property!.SetValue(SamplerFilter.Nearest);
        nearest.MipmapFilter.Property!.SetValue(SamplerFilter.Nearest);
        graph.Connect(nearest.Image, pass.DepthOutput);
        graph.Connect(view.In("depth"), nearest.Output);
        graph.Connect(view.Targets, colorImage.Output);
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, view.Out("color"));

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);

        Assert.That(description.PassNames.Count(name => name.StartsWith("Render")), Is.EqualTo(1));
        ushort[] pixels = ShaderNodeTestHelpers.Render(description, 1);
        Assert.That((float)BitConverter.UInt16BitsToHalf(pixels[0]), Is.EqualTo(0.25f).Within(1e-3f));
    }

    private const string StepCode = """
        #version 450
        layout(location=0) in vec2 uv;
        layout(location=0) out vec4 color;
        layout(set=0,binding=0) uniform sampler2D previous;
        layout(set=0,binding=1) uniform Step { float time; float delta; int index; float gain; } step_;
        void main() {
            color=texture(previous,uv)*vec4(1-step_.delta,1-step_.time*.1,1-float(step_.index)*.1,1)*step_.gain;
        }
        """;

    private static GraphModel CreateReplayGraph(out ShaderReplayNode replay)
    {
        var graph = new GraphModel();
        replay = graph.Add(new ShaderReplayNode());
        replay.StepsPerSecond.Property!.SetValue(10d);
        replay.ResetTime.Property!.SetValue(0.2d);
        var body = replay.Group.Value;
        var pass = body.Add(new ShaderFullscreenPassNode { Code = { Value = StepCode } });
        ShaderLoopInfoNode info = body.Nodes.OfType<ShaderLoopInfoNode>().Single();
        IOutputPort state = (IOutputPort)body.Input.Value!.Items[0];
        body.Connect(pass.In("previous"), state);
        body.Connect(pass.In("step_.time"), info.Time);
        body.Connect(pass.In("step_.delta"), info.Delta);
        body.Connect(pass.In("step_.index"), info.Index);
        ((DefaultInputPort<float>)pass.In("step_.gain")).GetProperty()!.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 0.5f } },
                new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 1f } }
            }
        };
        body.Connect((IInputPort)body.Output.Value!.Items[0], pass.Out("color"));
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, replay.Items.OfType<IOutputPort>().First());
        return graph;
    }

    private static ShaderGraphDescription ReplayReference(double time) => ShaderGraphBuilder.Build(time, static (builder, requested) =>
    {
        ShaderImage image = builder.Replay(TimeSpan.FromSeconds(requested), TimeSpan.FromSeconds(0.1),
            static b => b.Source,
            static (b, previous, step) =>
            {
                ShaderImage next = b.Image2D($"image{step.Index}", previous);
                next.RasterSource = previous;
                byte[] bytes = new byte[16];
                BitConverter.GetBytes((float)step.Time.TotalSeconds).CopyTo(bytes, 0);
                BitConverter.GetBytes((float)step.Delta.TotalSeconds).CopyTo(bytes, 4);
                BitConverter.GetBytes(step.Index).CopyTo(bytes, 8);
                BitConverter.GetBytes(0.5f + 0.5f * (float)step.Time.TotalSeconds).CopyTo(bytes, 12);
                ShaderBuffer parameters = b.Buffer($"step{step.Index}", bytes, 1);
                b.Fullscreen($"update{step.Index}", ShaderProgram.Fullscreen(new ShaderSource(StepCode)), pass =>
                {
                    pass.SampledImage(0, previous);
                    pass.UniformBuffer(1, parameters);
                    pass.ColorAttachment(0, next);
                });
                return next;
            }, TimeSpan.FromSeconds(0.2));
        return builder.Output(image);
    });

    [Test]
    public void AReplayGivesTheSamePictureForAnySeekOrderAfterSaving()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = CreateReplayGraph(out _);
        foreach (GraphModel candidate in new[] { graph, Restored(graph) })
        {
            ShaderReplayNode replay = candidate.Nodes.OfType<ShaderReplayNode>().Single();
            using var snapshot = new GraphSnapshot();
            foreach (double time in new[] { 0.55, 0.1, 0.45, 0.2, 0.55 })
            {
                var context = new CompositionContext(MediaTime.FromSeconds(time));
                snapshot.Build(candidate, context);
                snapshot.Evaluate(context);
                // Up to its reset time a replay has no steps, and with nothing connected it hands on nothing.
                if (time <= 0.2)
                {
                    Assert.That(ShaderNodeTestHelpers.Picture(candidate, snapshot), Is.Null, $"At {time}");
                    continue;
                }

                ShaderGraphDescription description = ShaderNodeTestHelpers.Record(candidate, snapshot);
                ShaderGraphDescription reference = ReplayReference(time);
                Assert.That(description.PassNames.Length, Is.EqualTo(reference.PassNames.Length));
                Assert.That(ShaderNodeTestHelpers.Render(description, 1), Is.EqualTo(ShaderNodeTestHelpers.Render(reference, 1)), $"At {time}");
            }
        }
    }

    [Test]
    public void AReplayLongerThanAStackCouldFollowStillRunsEveryStep()
    {
        VulkanTestEnvironment.EnsureAvailable();
        const int Steps = 5000;
        var graph = new GraphModel();
        var replay = graph.Add(new ShaderReplayNode());
        replay.StepsPerSecond.Property!.SetValue((double)Steps);
        graph.Connect(replay.Time, NodeGraphTestHelpers.Source(graph, 1f));
        var body = replay.Group.Value;
        var pass = body.Add(new ShaderFullscreenPassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(location=0) in vec2 uv;
                    layout(location=0) out vec4 color;
                    layout(set=0,binding=0) uniform sampler2D previous;
                    void main() { color=vec4(texture(previous,uv).r+1.0/8192.0,0.0,0.0,1.0); }
                    """
            }
        });
        pass.Format.Property!.SetValue(TextureFormat.RGBA32Float);
        body.Connect(pass.In("previous"), (IOutputPort)body.Input.Value!.Items[0]);
        body.Connect((IInputPort)body.Output.Value!.Items[0], pass.Out("color"));
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, replay.Items.OfType<IOutputPort>().First());

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);
        Assert.That(description.PassNames.Length, Is.EqualTo(Steps));
        ushort[] pixels = ShaderNodeTestHelpers.Render(description, 1);
        Assert.That((float)BitConverter.UInt16BitsToHalf(pixels[0]), Is.EqualTo(Steps / 8192f).Within(1e-3f));
    }

    private const string BlurCode = """
        #version 450
        layout(location=0) in vec2 uv;
        layout(location=0) out vec4 color;
        layout(set=0,binding=0) uniform sampler2D previous;
        void main() {
            vec2 texel=1.0/vec2(textureSize(previous,0));
            color=(texture(previous,uv-texel)+texture(previous,uv+texel))*0.5;
        }
        """;

    [Test]
    public void ARepeatChainsItsBodyLikeTheBuilder()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var graph = new GraphModel();
        var repeat = graph.Add(new ShaderRepeatNode());
        repeat.Count.Property!.SetValue(4);
        var body = repeat.Group.Value;
        var pass = body.Add(new ShaderFullscreenPassNode { Code = { Value = BlurCode } });
        body.Connect(pass.In("previous"), (IOutputPort)body.Input.Value!.Items[0]);
        body.Connect((IInputPort)body.Output.Value!.Items[0], pass.Out("color"));
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, repeat.Items.OfType<IOutputPort>().First());

        AssertMatches(graph, ShaderGraphBuilder.Build(0, static (builder, _) =>
            builder.Output(builder.Repeat(4, builder.Source, static (b, previous, index) =>
            {
                ShaderImage next = b.Image2D($"step{index}", previous);
                next.RasterSource = previous;
                b.Fullscreen($"blur{index}", ShaderProgram.Fullscreen(new ShaderSource(BlurCode)), pass =>
                {
                    pass.SampledImage(0, previous);
                    pass.ColorAttachment(0, next);
                });
                return next;
            }))));
    }

    [TestCase(0)]
    [TestCase(2)]
    public async Task AReplayFollowsTheClipsOwnClockInPreviewAndExport(int start)
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-replay-export", duration: MediaTime.FromSeconds(start + 1));
        ((IModifiableHierarchical)new TestHierarchicalRoot()).AddChild(files.Scene);
        GraphModel graph = CreateReplayGraph(out ShaderReplayNode replay);
        replay.ResetTime.Property!.SetValue(0d);
        var source = graph.Add(new FilterEffectInputNode());
        graph.Connect(replay.Items.OfType<IInputPort>().First(), source.Output);
        var effect = new NodeGraphFilterEffect();
        effect.Model.Value = graph;
        Populate(files, effect, MediaTime.FromSeconds(start));
        CoreSerializer.StoreToUri(files.Scene, files.Scene.Uri!);
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(files.Scene.Uri!);
        Scene normalized = CoreSerializer.RestoreFromUri<Scene>(files.Scene.Uri!);
        ((IModifiableHierarchical)new TestHierarchicalRoot()).AddChild(restored);
        ((IModifiableHierarchical)new TestHierarchicalRoot()).AddChild(normalized);
        normalized.Children.Single().Start.Value = MediaTime.Zero;
        using var preview = new SceneRenderer(restored, RenderIntent.Preview, 1);
        using var reference = new SceneRenderer(normalized, RenderIntent.Preview, 1);
        var frames = new Dictionary<int, ushort[]>();
        foreach (int frame in new[] { 3, 0, 2, 1, 3 })
        {
            frames[frame] = RenderScene(preview, MediaTime.FromFrames(start * 4 + frame, new Rational(4)), 1);
            Assert.That(frames[frame], Is.EqualTo(RenderScene(reference, MediaTime.FromFrames(frame, new Rational(4)), 1)));
        }

        Assert.That(frames[0], Is.Not.EqualTo(frames[3]));
        using SceneRenderer exporter = ExportRendererFactory.Create(restored, 1);
        using var progress = new Subject<MediaTime>();
        using var provider = new FrameProviderImpl(restored, new Rational(4), exporter, progress);
        for (int frame = 0; frame < (start + 1) * 4; frame++)
        {
            using Bitmap? bitmap = await provider.ReadNextFrame().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.That(bitmap, Is.Not.Null);
            if (frame >= start * 4)
                Assert.That(bitmap!.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(frames[frame - start * 4]));
            else
                Assert.That(bitmap!.GetPixelSpan<ushort>().ToArray(), Is.All.EqualTo((ushort)0));
        }

        Assert.That(await provider.ReadNextFrame(), Is.Null);
    }

    [TestCase(1f)]
    [TestCase(2f)]
    public async Task AnAnimatedPassMatchesTheScriptEffectAfterSaveSeekAndExport(float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-nodes-export", duration: MediaTime.FromSeconds(1));
        using var referenceFiles = new SceneHistoryHarness("shader-script-reference", duration: MediaTime.FromSeconds(1));
        var effect = new NodeGraphFilterEffect();
        GraphModel graph = effect.Model.Value!;
        var input = graph.Add(new FilterEffectInputNode());
        var pass = graph.Add(new ShaderFullscreenPassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(location=0) in vec2 uv;
                    layout(location=0) out vec4 color;
                    layout(set=0,binding=0) uniform sampler2D sourceImage;
                    layout(set=0,binding=1) uniform Parameters { float amount; } parameters;
                    void main() { color=texture(sourceImage,uv)*parameters.amount; }
                    """
            }
        });
        ((DefaultInputPort<float>)pass.In("parameters.amount")).GetProperty()!.Animation = new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 0.25f } },
                new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = 1f } }
            }
        };
        graph.Connect(pass.In("sourceImage"), input.Output);
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        Populate(files, effect);
        Populate(referenceFiles, new GLSLScriptEffect
        {
            FragmentShader =
            {
                Value = """
                    #version 450
                    layout(location=0) in vec2 uv;
                    layout(location=0) out vec4 color;
                    layout(set=0,binding=0) uniform sampler2D sourceImage;
                    layout(push_constant) uniform Timeline { float progress; float duration; float time;
                        float width; float height; float scale; } pc;
                    void main() { color=texture(sourceImage,uv)*(0.25+0.75*pc.time); }
                    """
            }
        });
        CoreSerializer.StoreToUri(files.Scene, files.Scene.Uri!);
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(files.Scene.Uri!);
        using var originalRenderer = new SceneRenderer(files.Scene, RenderIntent.Preview, scale);
        using var restoredRenderer = new SceneRenderer(restored, RenderIntent.Preview, scale);
        using var referenceRenderer = new SceneRenderer(referenceFiles.Scene, RenderIntent.Preview, scale);
        var frames = new Dictionary<int, ushort[]>();
        foreach (int frame in new[] { 2, 0, 3, 1, 2 })
        {
            var time = MediaTime.FromFrames(frame, new Rational(4));
            ushort[] expected = RenderScene(referenceRenderer, time, scale);
            Assert.That(RenderScene(originalRenderer, time, scale), Is.EqualTo(expected), $"Original at {frame}");
            Assert.That(RenderScene(restoredRenderer, time, scale), Is.EqualTo(expected), $"Restored at {frame}");
            frames[frame] = expected;
        }

        Assert.That(frames[0], Has.Some.Not.EqualTo((ushort)0));
        Assert.That(frames[0], Is.Not.EqualTo(frames[2]));
        using SceneRenderer exporter = ExportRendererFactory.Create(restored, scale);
        using var progress = new Subject<MediaTime>();
        using var provider = new FrameProviderImpl(restored, new Rational(4), exporter, progress);
        for (int frame = 0; frame < 4; frame++)
        {
            using Bitmap? bitmap = await provider.ReadNextFrame().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.That(bitmap, Is.Not.Null);
            Assert.That(bitmap!.GetPixelSpan<ushort>().ToArray(), Is.EqualTo(frames[frame]), $"Export at {frame}");
        }

        Assert.That(await provider.ReadNextFrame(), Is.Null);
    }

    [Test]
    public void AFailedGraphDrawsNothingAndOthersKeepRendering()
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-failed", duration: MediaTime.FromSeconds(1));
        var effect = new NodeGraphFilterEffect();
        GraphModel graph = effect.Model.Value!;
        var input = graph.Add(new FilterEffectInputNode());
        var pass = graph.Add(new ShaderFullscreenPassNode());
        graph.Connect(pass.In("source"), input.Output);
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        Populate(files, effect);
        using var renderer = new SceneRenderer(files.Scene, RenderIntent.Preview, 1);
        ushort[] working = RenderScene(renderer, MediaTime.Zero, 1);
        Assert.That(working, Has.Some.Not.EqualTo((ushort)0));

        pass.Code.Value = "broken";
        ushort[] failed = RenderScene(renderer, MediaTime.Zero, 1);
        Assert.That(pass.Error.Value, Is.Not.Null.And.Not.Empty);
        Assert.That(failed, Is.All.EqualTo((ushort)0), "A failure must not pass for a working result.");

        pass.Code.Value = ShaderFullscreenPassNode.DefaultCode;
        Assert.That(RenderScene(renderer, MediaTime.Zero, 1), Is.EqualTo(working));
    }

    [Test]
    public void AGraphWithNoInputConnectedFillsTheFrame()
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-generator", duration: MediaTime.FromSeconds(1));
        files.Scene.FrameSize.Value = new PixelSize(48, 40);
        var drawable = new NodeGraphDrawable();
        GraphModel graph = drawable.Model.Value!;
        var pass = graph.Add(new ShaderFullscreenPassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(location=0) in vec2 uv;
                    layout(location=0) out vec4 color;
                    void main() { color=vec4(uv,0,1); }
                    """
            }
        });
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        files.AddElement(MediaTime.Zero, MediaTime.FromSeconds(1)).AddObject(drawable);
        using var renderer = new SceneRenderer(files.Scene, RenderIntent.Preview, 1);

        ushort[] pixels = RenderScene(renderer, MediaTime.Zero, 1);

        Assert.That(pass.Error.Value, Is.Null);
        Assert.That(pixels, Has.Length.EqualTo(48 * 40 * 4));
        Assert.That(Enumerable.Range(0, 48 * 40).Select(i => (float)BitConverter.UInt16BitsToHalf(pixels[i * 4 + 3])), Is.All.EqualTo(1f));
        Assert.That((float)BitConverter.UInt16BitsToHalf(pixels[47 * 4]), Is.GreaterThan((float)BitConverter.UInt16BitsToHalf(pixels[0])));
    }

    [Test]
    public void APictureFromElsewhereIsReadAtTheSamePlaceAsTheSource()
    {
        VulkanTestEnvironment.EnsureAvailable();
        const string mask = """
            #version 450
            layout(location=0) in vec2 uv;
            layout(location=0) out vec4 color;
            layout(set=0,binding=0) uniform sampler2D source;
            layout(set=0,binding=1) uniform sampler2D matte;
            void main() { color=texture(source,uv)*texture(matte,uv).a; }
            """;
        using var files = new SceneHistoryHarness("shader-picture", duration: MediaTime.FromSeconds(1));
        var effect = new NodeGraphFilterEffect();
        GraphModel graph = effect.Model.Value!;
        var input = graph.Add(new FilterEffectInputNode());
        var geometry = graph.Add(new RectGeometryNode());
        var width = (IInputPort)geometry.Items.Single(item => item.Name.Value == "Width");
        var height = (IInputPort)geometry.Items.Single(item => item.Name.Value == "Height");
        width.Property!.SetValue(1000f);
        height.Property!.SetValue(1000f);
        var shape = graph.Add(new GeometryShapeNode());
        shape.Fill.Property!.SetValue(new SolidColorBrush(Colors.White));
        graph.Connect(shape.Geometry, geometry.OutputPort);
        var pass = graph.Add(new ShaderFullscreenPassNode { Code = { Value = mask } });
        graph.Connect(pass.In("source"), input.Output);
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        Populate(files, effect);
        using var renderer = new SceneRenderer(files.Scene, RenderIntent.Preview, 1);
        const int frameWidth = 48;
        float Alpha(ushort[] pixels, int x, int y) => (float)BitConverter.UInt16BitsToHalf(pixels[(y * frameWidth + x) * 4 + 3]);

        // With nothing connected the picture is the source itself, which shows where the source lies.
        ushort[] whole = RenderScene(renderer, MediaTime.Zero, 1);
        Assert.That(pass.Error.Value, Is.Null);
        int[] columns = [.. Enumerable.Range(0, frameWidth).Where(x => Enumerable.Range(0, 40).Any(y => Alpha(whole, x, y) > 0.5f))];
        int[] rows = [.. Enumerable.Range(0, 40).Where(y => Enumerable.Range(0, frameWidth).Any(x => Alpha(whole, x, y) > 0.5f))];
        Assert.That(columns, Has.Length.EqualTo(24));
        Assert.That(rows, Has.Length.EqualTo(16));

        // A rectangle covering everything leaves it unchanged.
        graph.Connect(pass.In("matte"), shape.Output);
        Assert.That(RenderScene(renderer, MediaTime.Zero, 1), Is.EqualTo(whole));

        // An effect works in its drawable's own coordinates, so a rectangle ending at 12 ends halfway across the source.
        width.Property!.SetValue(12f);
        ushort[] half = RenderScene(renderer, MediaTime.Zero, 1);
        Assert.That(Alpha(half, columns[0] + 2, rows[8]), Is.EqualTo(1f).Within(1e-3f));
        Assert.That(Alpha(half, columns[0] + 11, rows[8]), Is.EqualTo(1f).Within(1e-3f));
        Assert.That(Alpha(half, columns[0] + 13, rows[8]), Is.EqualTo(0f).Within(1e-3f));
        Assert.That(Alpha(half, columns[^1], rows[8]), Is.EqualTo(0f).Within(1e-3f));

        // One that misses it entirely leaves nothing, though the output is still there to draw.
        height.Property!.SetValue(1f);
        width.Property!.SetValue(1f);
        Assert.That(RenderScene(renderer, MediaTime.Zero, 1)[((rows[8] * frameWidth + columns[12]) * 4) + 3], Is.Zero);

        // Saved and loaded, the same graph gives the same picture.
        width.Property!.SetValue(12f);
        height.Property!.SetValue(1000f);
        CoreSerializer.StoreToUri(files.Scene, files.Scene.Uri!);
        Scene restored = CoreSerializer.RestoreFromUri<Scene>(files.Scene.Uri!);
        using var restoredRenderer = new SceneRenderer(restored, RenderIntent.Preview, 1);
        Assert.That(RenderScene(restoredRenderer, MediaTime.Zero, 1), Is.EqualTo(half));
    }

    [Test]
    public void AGraphCanMakeItsPictureFromAnotherPictureAlone()
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-picture-generator", duration: MediaTime.FromSeconds(1));
        files.Scene.FrameSize.Value = new PixelSize(48, 40);
        var drawable = new NodeGraphDrawable();
        GraphModel graph = drawable.Model.Value!;
        var geometry = graph.Add(new RectGeometryNode());
        ((IInputPort)geometry.Items.Single(item => item.Name.Value == "Width")).Property!.SetValue(24f);
        ((IInputPort)geometry.Items.Single(item => item.Name.Value == "Height")).Property!.SetValue(40f);
        var shape = graph.Add(new GeometryShapeNode());
        shape.Fill.Property!.SetValue(new SolidColorBrush(Colors.White));
        graph.Connect(shape.Geometry, geometry.OutputPort);
        var pass = graph.Add(new ShaderFullscreenPassNode());
        graph.Connect(pass.In("source"), shape.Output);
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        files.AddElement(MediaTime.Zero, MediaTime.FromSeconds(1)).AddObject(drawable);
        using var renderer = new SceneRenderer(files.Scene, RenderIntent.Preview, 1);

        ushort[] pixels = RenderScene(renderer, MediaTime.Zero, 1);

        Assert.That(pass.Error.Value, Is.Null);
        Assert.That((float)BitConverter.UInt16BitsToHalf(pixels[(20 * 48 + 12) * 4 + 3]), Is.EqualTo(1f).Within(1e-3f));
        Assert.That((float)BitConverter.UInt16BitsToHalf(pixels[(20 * 48 + 36) * 4 + 3]), Is.EqualTo(0f).Within(1e-3f));
    }

    private const string MergeCode = """
        #version 450
        layout(location=0) in vec2 uv;
        layout(location=0) out vec4 color;
        layout(set=0,binding=0) uniform sampler2D a;
        layout(set=0,binding=1) uniform sampler2D b;
        void main() { color=max(texture(a,uv),texture(b,uv)); }
        """;

    [TestCase(0f)]
    [TestCase(1f)]
    public void InputsAreReadAtTheSamePlaceWhateverTheyAndTheOutputCover(float resolutionOfTheSecondPass)
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-aligned", duration: MediaTime.FromSeconds(1));
        var effect = new NodeGraphFilterEffect();
        GraphModel graph = effect.Model.Value!;
        var input = graph.Add(new FilterEffectInputNode());
        var padded = graph.Add(new ShaderFullscreenPassNode());
        padded.Padding.Property!.SetValue(0f);
        var merge = graph.Add(new ShaderFullscreenPassNode { Code = { Value = MergeCode } });
        graph.Connect(padded.In("source"), input.Output);
        graph.Connect(merge.In("b"), padded.Out("color"));
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, merge.Out("color"));
        Populate(files, effect);
        using var renderer = new SceneRenderer(files.Scene, RenderIntent.Preview, 1);
        ushort[] plain = RenderScene(renderer, MediaTime.Zero, 1);
        Assert.That(plain, Has.Some.Not.EqualTo((ushort)0));

        // The pass that reads the source covers more than it does, and the pass after reads both of them.
        padded.Padding.Property!.SetValue(6f);
        if (resolutionOfTheSecondPass > 0) merge.Padding.Property!.SetValue(10f);
        ushort[] aligned = RenderScene(renderer, MediaTime.Zero, 1);

        Assert.That(merge.Error.Value, Is.Null);
        // Read at another size, the coordinates round differently on a software device, which leaves traces of about
        // 1e-6 across the picture's edge. A move or a shrink shows at full strength.
        for (int index = 0; index < plain.Length; index++)
            Assert.That(Channel(aligned, index), Is.EqualTo(Channel(plain, index)).Within(1f / 65536),
                $"Space around a picture must not move or shrink it, at {index}.");
    }

    private static void Populate(SceneHistoryHarness files, FilterEffect effect, MediaTime? start = null)
    {
        files.Scene.FrameSize.Value = new PixelSize(48, 40);
        files.AddElement(start ?? MediaTime.Zero, files.Scene.Duration.Value - (start ?? MediaTime.Zero)).AddObject(new RectShape
        {
            Width = { Value = 24 },
            Height = { Value = 16 },
            Fill = { Value = new SolidColorBrush(Colors.White) },
            FilterEffect = { Value = effect }
        });
    }

    private static ushort[] RenderScene(SceneRenderer renderer, MediaTime time, float scale)
        => VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            renderer.Render(renderer.Compositor.EvaluateGraphics(time));
            using Bitmap original = renderer.Snapshot();
            Bitmap delivered = SupersampleDownscaler.ToFrameSize(original, renderer.FrameSize, scale);
            try { return delivered.GetPixelSpan<ushort>().ToArray(); }
            finally { if (!ReferenceEquals(original, delivered)) delivered.Dispose(); }
        });
}
