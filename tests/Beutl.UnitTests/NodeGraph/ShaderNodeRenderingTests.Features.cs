using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.NodeGraph.Nodes.Utilities;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.NodeGraph;

public partial class ShaderNodeRenderingTests
{
    private const string StageVertexCode = """
        #version 450
        layout(location=0) out vec4 tint;
        void main() {
            vec2 p=vec2((gl_VertexIndex<<1)&2,gl_VertexIndex&2);
            gl_Position=vec4(p*2-1,0,1);
            tint=vec4(0.5,0.25,0.125,1);
        }
        """;

    private const string StageFragmentCode = """
        #version 450
        layout(location=0) in vec4 tint;
        layout(location=0) out vec4 color;
        void main() { color=tint; }
        """;

    private const string StageGeometryCode = """
        #version 450
        layout(triangles) in;
        layout(triangle_strip, max_vertices=3) out;
        layout(location=0) in vec4 inputTint[];
        layout(location=0) out vec4 tint;
        void main() {
            for(int i=0;i<3;i++) {
                gl_Position=gl_in[i].gl_Position;
                tint=vec4(inputTint[i].rgb*0.5,inputTint[i].a);
                EmitVertex();
            }
            EndPrimitive();
        }
        """;

    private const string StageControlCode = """
        #version 450
        layout(vertices=3) out;
        layout(location=0) in vec4 inputTint[];
        layout(location=0) out vec4 tint[];
        void main() {
            gl_out[gl_InvocationID].gl_Position=gl_in[gl_InvocationID].gl_Position;
            tint[gl_InvocationID]=inputTint[gl_InvocationID];
            gl_TessLevelOuter[0]=1; gl_TessLevelOuter[1]=1; gl_TessLevelOuter[2]=1;
            gl_TessLevelInner[0]=1;
        }
        """;

    private const string StageEvaluationCode = """
        #version 450
        layout(triangles, equal_spacing, ccw) in;
        layout(location=0) in vec4 inputTint[];
        layout(location=0) out vec4 tint;
        void main() {
            gl_Position=gl_in[0].gl_Position*gl_TessCoord.x+gl_in[1].gl_Position*gl_TessCoord.y+gl_in[2].gl_Position*gl_TessCoord.z;
            tint=vec4(inputTint[0].rg,0.75,inputTint[0].a);
        }
        """;

    private static float Channel(ushort[] pixels, int index) => (float)BitConverter.UInt16BitsToHalf(pixels[index]);

    private static GraphModel WithOutput(Func<GraphModel, IOutputPort> image)
    {
        var graph = new GraphModel();
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, image(graph));
        return graph;
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void OptionalStagesDrawLikeTheBuilder(bool geometry, bool tessellation)
    {
        VulkanTestEnvironment.EnsureAvailable();
        ShaderCapabilities capabilities = VulkanTestEnvironment.InvokeOnRenderThread(
            () => ShaderCapabilities.Query(GraphicsContextFactory.SharedContext!));
        if (geometry && !capabilities.GeometryShaders || tessellation && !capabilities.TessellationShaders)
            Assert.Ignore("This device does not run these stages.");

        GraphModel graph = WithOutput(graph =>
        {
            var pass = graph.Add(new ShaderExtendedRenderPassNode
            {
                Vertex = { Value = StageVertexCode },
                Fragment = { Value = StageFragmentCode },
                Geometry = { Value = geometry ? StageGeometryCode : "" },
                TessellationControl = { Value = tessellation ? StageControlCode : "" },
                TessellationEvaluation = { Value = tessellation ? StageEvaluationCode : "" }
            });
            Assert.That(pass.Error.Value, Is.Null);
            if (tessellation)
            {
                var rasterizer = graph.Add(new ShaderRasterizerNode());
                rasterizer.Topology.Property!.SetValue(PrimitiveTopology.PatchList);
                graph.Connect(pass.Settings, rasterizer.Output);
            }

            return pass.Out("color");
        });

        AssertMatches(graph, ShaderGraphBuilder.Build((geometry, tessellation), static (builder, stages) =>
        {
            ShaderImage output = builder.Image2D("output", builder.Source);
            output.RasterSource = builder.Source;
            builder.Render("stages", ShaderProgram.Graphics(new ShaderSource(StageVertexCode), new ShaderSource(StageFragmentCode),
                stages.geometry ? new ShaderSource(StageGeometryCode) : null,
                stages.tessellation ? new ShaderSource(StageControlCode) : null,
                stages.tessellation ? new ShaderSource(StageEvaluationCode) : null), pass =>
            {
                pass.ColorAttachment(0, output);
                if (stages.tessellation) pass.PatchControlPoints(3);
                pass.Draw(3);
            });
            return builder.Output(output);
        }));
    }

    [Test]
    public void AMipLevelOfAFixedImageIsReadThroughAView()
    {
        VulkanTestEnvironment.EnsureAvailable();
        var value = new Vector4(0.25f, 0.5f, 0.75f, 1);
        GraphModel graph = WithOutput(graph =>
        {
            var image = graph.Add(new ShaderFixedImageNode());
            image.Width.Property!.SetValue(8);
            image.Height.Property!.SetValue(8);
            image.MipLevels.Property!.SetValue(3);
            var clear = graph.Add(new ShaderClearImageNode());
            clear.Value.Property!.SetValue(value);
            graph.Connect(clear.Image, image.Output);
            var mipmaps = graph.Add(new ShaderGenerateMipmapsNode());
            graph.Connect(mipmaps.Image, clear.Output);
            var view = graph.Add(new ShaderImageViewNode());
            view.BaseMip.Property!.SetValue(2);
            graph.Connect(view.Image, mipmaps.Output);
            var pass = graph.Add(new ShaderFullscreenPassNode());
            graph.Connect(pass.In("source"), view.Output);
            return pass.Out("color");
        });

        ShaderGraphDescription reference = ShaderGraphBuilder.Build(value, static (builder, value) =>
        {
            ShaderImage image = builder.Image("image", new ImageDescription(ImageKind.Image2D, 8, 8, TextureFormat.RGBA16Float) { MipLevels = 3 });
            builder.Clear("clear", image, ImageClearValue.Float(value));
            builder.GenerateMipmaps("mipmaps", image);
            ShaderImage view = builder.View("view", image, ImageKind.Image2D, baseMip: 2);
            ShaderImage output = builder.Image2D("output", builder.Source);
            output.RasterSource = builder.Source;
            builder.Fullscreen("pass", ShaderProgram.Fullscreen(new ShaderSource(ShaderFullscreenPassNode.DefaultCode)), pass =>
            {
                pass.SampledImage(0, view);
                pass.ColorAttachment(0, output);
            });
            return builder.Output(output);
        });

        AssertMatches(graph, reference);
        // The middle of the frame: a texture this small meets its transparent border near the edges.
        ushort[] pixels = ShaderNodeTestHelpers.Render(reference, 1);
        int middle = (12 * 32 + 16) * 4;
        Assert.That(Enumerable.Range(middle, 4).Select(i => Channel(pixels, i)), Is.EqualTo(new[] { 0.25f, 0.5f, 0.75f, 1f }).Within(1e-3f));
    }

    [Test]
    public void ACopyConvertsToAnEncodingAndBack()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var pass = graph.Add(new ShaderFullscreenPassNode());
            var destination = graph.Add(new ShaderImageNode());
            var copy = graph.Add(new ShaderCopyImageNode());
            graph.Connect(copy.Source, pass.Out("color"));
            graph.Connect(copy.Destination, destination.Output);
            var encode = graph.Add(new ShaderConvertColorNode());
            graph.Connect(encode.Image, copy.Output);
            var decode = graph.Add(new ShaderConvertColorNode());
            decode.From.Property!.SetValue(ColorEncoding.SrgbStraight);
            decode.To.Property!.SetValue(ColorEncoding.LinearPremultiplied);
            graph.Connect(decode.Image, encode.Output);
            return decode.Output;
        });

        AssertMatches(graph, ShaderGraphBuilder.Build(0, static (builder, _) =>
        {
            ShaderImage first = builder.Image2D("first", builder.Source);
            first.RasterSource = builder.Source;
            builder.Fullscreen("pass", ShaderProgram.Fullscreen(new ShaderSource(ShaderFullscreenPassNode.DefaultCode)), pass =>
            {
                pass.SampledImage(0, builder.Source);
                pass.ColorAttachment(0, first);
            });
            ShaderImage destination = builder.Image2D("destination", builder.Source);
            destination.RasterSource = builder.Source;
            builder.Copy("copy", first, destination);
            ShaderImage encoded = builder.ConvertColor("encode", destination, ColorEncoding.LinearPremultiplied, ColorEncoding.SrgbStraight);
            return builder.Output(builder.ConvertColor("decode", encoded, ColorEncoding.SrgbStraight, ColorEncoding.LinearPremultiplied));
        }));
    }

    private const string TriangleVertexCode = """
        #version 450
        void main() {
            vec2 corners[3]=vec2[](vec2(-1,-1),vec2(1,-1),vec2(-1,1));
            gl_Position=vec4(corners[gl_VertexIndex],0,1);
        }
        """;

    private const string PaintCode = """
        #version 450
        layout(location=0) out vec4 color;
        void main() { color=vec4(0.5,0,0,0.5); }
        """;

    [TestCase(FrontFace.Clockwise)]
    [TestCase(FrontFace.CounterClockwise)]
    public void WindingStencilAndBlendingShapeWhatAPassPaints(FrontFace front)
    {
        VulkanTestEnvironment.EnsureAvailable();
        TextureFormat format = VulkanTestEnvironment.InvokeOnRenderThread(() =>
            ShaderCapabilities.Query(GraphicsContextFactory.SharedContext!).SupportsImage(
                new ImageDescription(ImageKind.Image2D, 4, 4, TextureFormat.Depth24Stencil8), ImageUsage.DepthStencilAttachment)
                ? TextureFormat.Depth24Stencil8 : TextureFormat.Depth32FloatStencil8);

        GraphModel graph = WithOutput(graph =>
        {
            var background = graph.Add(new ShaderFullscreenPassNode());
            var depthImage = graph.Add(new ShaderImageNode());
            depthImage.Format.Property!.SetValue(format);
            var replace = graph.Add(new ShaderStencilNode());
            replace.Pass.Property!.SetValue(StencilOperation.Replace);
            replace.Reference.Property!.SetValue(5u);
            var writeMask = graph.Add(new ShaderDepthNode());
            writeMask.ClearStencil.Property!.SetValue(2u);
            writeMask.Test.Property!.SetValue(false);
            writeMask.Write.Property!.SetValue(false);
            graph.Connect(writeMask.Image, depthImage.Output);
            graph.Connect(writeMask.StencilFront, replace.Output);
            var rasterizer = graph.Add(new ShaderRasterizerNode());
            rasterizer.Cull.Property!.SetValue(CullMode.Back);
            rasterizer.Front.Property!.SetValue(front);
            var mask = graph.Add(new ShaderRenderPassNode
            {
                Vertex = { Value = TriangleVertexCode },
                Fragment = { Value = "#version 450\nvoid main() {}\n" }
            });
            Assert.That(mask.Error.Value, Is.Null);
            graph.Connect(mask.Settings, writeMask.Output);
            graph.Connect(mask.Settings, rasterizer.Output);
            var equal = graph.Add(new ShaderStencilNode());
            equal.Compare.Property!.SetValue(CompareFunction.Equal);
            equal.WriteMask.Property!.SetValue(0u);
            equal.Reference.Property!.SetValue(5u);
            var readMask = graph.Add(new ShaderDepthNode());
            readMask.Load.Property!.SetValue(AttachmentLoadOp.Load);
            readMask.Test.Property!.SetValue(false);
            readMask.Write.Property!.SetValue(false);
            graph.Connect(readMask.Image, mask.DepthOutput);
            graph.Connect(readMask.StencilFront, equal.Output);
            var paint = graph.Add(new ShaderFullscreenPassNode { Code = { Value = PaintCode } });
            graph.Connect(paint.Targets, background.Out("color"));
            graph.Connect(paint.Settings, readMask.Output);
            graph.Connect(paint.Settings, graph.Add(new ShaderBlendNode()).Output);
            return paint.Out("color");
        });

        ShaderGraphDescription reference = ShaderGraphBuilder.Build((format, front), static (builder, state) =>
        {
            ShaderImage color = builder.Image2D("color", builder.Source);
            color.RasterSource = builder.Source;
            builder.Fullscreen("background", ShaderProgram.Fullscreen(new ShaderSource(ShaderFullscreenPassNode.DefaultCode)), pass =>
            {
                pass.SampledImage(0, builder.Source);
                pass.ColorAttachment(0, color);
            });
            ShaderImage depth = builder.Image2D("depth", builder.Source, state.format);
            depth.RasterSource = builder.Source;
            builder.ClearDepth("depth.clear", depth);
            var replace = new StencilFaceState(CompareFunction.Always, StencilOperation.Keep, StencilOperation.Keep, StencilOperation.Replace, Reference: 5);
            builder.Render("mask", ShaderProgram.Graphics(new ShaderSource(TriangleVertexCode), new ShaderSource("#version 450\nvoid main() {}\n")), pass =>
            {
                pass.DepthAttachment(depth, clearStencil: 2);
                pass.DepthStencil(new(false, false, StencilEnabled: true, Front: replace, Back: replace));
                pass.Cull(CullMode.Back, state.front);
                pass.Draw(3);
            });
            var equal = replace with { Compare = CompareFunction.Equal, Pass = StencilOperation.Keep, WriteMask = 0 };
            builder.Fullscreen("paint", ShaderProgram.Fullscreen(new ShaderSource(PaintCode)), pass =>
            {
                pass.ColorAttachment(0, color, AttachmentLoadOp.Load);
                pass.DepthAttachment(depth, AttachmentLoadOp.Load);
                pass.DepthStencil(new(false, false, StencilEnabled: true, Front: equal, Back: equal));
                pass.Blend(BlendState.PremultipliedAlpha);
            });
            return builder.Output(color);
        });

        AssertMatches(graph, reference);
    }

    [Test]
    public void ImageParametersGiveAPassWhatAlignedInputsGiveTheRest()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel aligned = WithOutput(graph =>
        {
            var pass = graph.Add(new ShaderFullscreenPassNode());
            pass.Padding.Property!.SetValue(6f);
            return pass.Out("color");
        });
        GraphModel manual = WithOutput(graph =>
        {
            var target = graph.Add(new ShaderImageNode());
            target.Padding.Property!.SetValue(6f);
            var from = graph.Add(new ShaderImageParametersNode());
            var to = graph.Add(new ShaderImageParametersNode());
            graph.Connect(to.Image, target.Output);
            var pass = graph.Add(new ShaderFullscreenPassNode
            {
                Code =
                {
                    Value = """
                        #version 450
                        layout(location=0) in vec2 uv;
                        layout(location=0) out vec4 color;
                        layout(set=0,binding=0) uniform sampler2D source;
                        layout(set=0,binding=1) uniform From { vec4 logical; vec4 raster; vec4 size; } from_;
                        layout(set=0,binding=2) uniform To { vec4 logical; vec4 raster; vec4 size; } to;
                        void main() {
                            vec2 position=to.raster.xy+uv*to.raster.zw;
                            color=texture(source,(position-from_.raster.xy)/from_.raster.zw);
                        }
                        """
                }
            });
            graph.Connect(pass.In("from_"), from.Output);
            graph.Connect(pass.In("to"), to.Output);
            graph.Connect(pass.Targets, target.Output);
            return pass.Out("color");
        });

        ushort[] expected = ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(aligned), 1);
        Assert.That(expected, Has.Length.EqualTo(44 * 36 * 4));
        float Alpha(ushort[] pixels, int x, int y) => Channel(pixels, ((y + 6) * 44 + x + 6) * 4 + 3);
        Assert.That(Alpha(expected, 4, 4), Is.EqualTo(1f).Within(1e-3f), "The picture is where it was.");
        Assert.That(Alpha(expected, 27, 19), Is.EqualTo(1f).Within(1e-3f));
        Assert.That(Alpha(expected, 3, 3), Is.Zero.Within(1e-3f));
        Assert.That(Alpha(expected, 28, 20), Is.Zero.Within(1e-3f));
        Assert.That(ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(manual), 1), Is.EqualTo(expected));
    }

    [Test]
    public void AnImageCanTakeItsShapeFromAnImageNothingReads()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var shape = graph.Add(new ShaderFixedImageNode());
            shape.Width.Property!.SetValue(8);
            shape.Height.Property!.SetValue(8);
            var view = graph.Add(new ShaderImageViewNode());
            graph.Connect(view.Image, shape.Output);
            var target = graph.Add(new ShaderImageNode());
            graph.Connect(target.Like, view.Output);
            var clear = graph.Add(new ShaderClearImageNode());
            clear.Value.Property!.SetValue(new Vector4(0.25f, 0, 0, 1));
            graph.Connect(clear.Image, target.Output);
            return clear.Output;
        });

        ushort[] pixels = ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(graph), 1);

        Assert.That(Channel(pixels, 0), Is.EqualTo(0.25f).Within(1e-3f));
    }

    private const string PackCode = """
        #version 450
        layout(location=0) out vec4 color;
        layout(set=0,binding=0) uniform Packed { vec2 first; float second; float third; } packed_;
        void main() { color=vec4(packed_.first.y,packed_.second,packed_.third,1); }
        """;

    [Test]
    public void PackedNumbersFillABlockInOrder()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var vector = graph.Add(new ShaderVectorNode());
            vector.X.Property!.SetValue(0.125f);
            vector.Y.Property!.SetValue(0.25f);
            var data = graph.Add(new ShaderDataNode());
            data.Text.Property!.SetValue("0.5");
            var pack = graph.Add(new ShaderPackNode());
            graph.Connect(pack.Values, vector.Vector2);
            graph.Connect(pack.Values, NodeGraphTestHelpers.Source(graph, 0.375f));
            graph.Connect(pack.Values, data.Output);
            var buffer = graph.Add(new ShaderBufferNode());
            graph.Connect(buffer.Data, pack.Output);
            var pass = graph.Add(new ShaderFullscreenPassNode { Code = { Value = PackCode } });
            Assert.That(pass.Error.Value, Is.Null);
            graph.Connect(pass.In("packed_"), buffer.Output);
            return pass.Out("color");
        });

        ushort[] pixels = ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(graph), 1);

        Assert.That(Enumerable.Range(0, 4).Select(i => Channel(pixels, i)), Is.EqualTo(new[] { 0.25f, 0.375f, 0.5f, 1f }).Within(1e-3f));
    }

    [Test]
    public void AColorAndAVectorArriveAsTheShaderWorksInThem()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var tint = graph.Add(new ShaderColorNode());
            tint.Color.Property!.SetValue(Color.FromArgb(128, 255, 128, 0));
            var offset = graph.Add(new ShaderVectorNode());
            offset.Y.Property!.SetValue(0.75f);
            var pass = graph.Add(new ShaderFullscreenPassNode
            {
                Code =
                {
                    Value = """
                        #version 450
                        layout(location=0) out vec4 color;
                        layout(set=0,binding=0) uniform Parameters { vec4 tint; vec2 offset; } parameters;
                        void main() { color=vec4(parameters.tint.g,parameters.tint.a,parameters.offset.y,1); }
                        """
                }
            });
            graph.Connect(pass.In("parameters.tint"), tint.Output);
            graph.Connect(pass.In("parameters.offset"), offset.Vector2);
            return pass.Out("color");
        });

        ushort[] pixels = ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(graph), 1);

        Assert.That(Channel(pixels, 0), Is.EqualTo(Color.SrgbToLinear(128 / 255f)).Within(1e-3f), "Linear green.");
        Assert.That(Channel(pixels, 1), Is.EqualTo(128 / 255f).Within(1e-3f), "Straight alpha.");
        Assert.That(Channel(pixels, 2), Is.EqualTo(0.75f).Within(1e-3f));
    }

    [Test]
    public void StorageNothingConnectsStartsEmpty()
    {
        VulkanTestEnvironment.EnsureAvailable();
        GraphModel graph = WithOutput(graph =>
        {
            var once = graph.Add(new ShaderDispatchNode { Mode = { Value = ShaderDispatchMode.Workgroups } });
            var compute = graph.Add(new ShaderComputePassNode
            {
                Code =
                {
                    Value = """
                        #version 450
                        layout(local_size_x=1) in;
                        layout(set=0,binding=0,std430) buffer Histogram { uint bins[4]; } histogram;
                        layout(set=0,binding=1,rgba16f) uniform image2D picture;
                        void main() {
                            atomicAdd(histogram.bins[1],3u);
                            imageStore(picture,ivec2(0),imageLoad(picture,ivec2(0))+vec4(0.25,0,0,1));
                        }
                        """
                }
            });
            Assert.That(compute.Error.Value, Is.Null);
            graph.Connect(compute.Settings, once.Output);
            var show = graph.Add(new ShaderFullscreenPassNode
            {
                Code =
                {
                    Value = """
                        #version 450
                        layout(location=0) out vec4 color;
                        layout(set=0,binding=0,std430) readonly buffer Histogram { uint bins[4]; } histogram;
                        layout(set=0,binding=1) uniform sampler2D picture;
                        void main() { color=vec4(float(histogram.bins[1])/4.0,texelFetch(picture,ivec2(0),0).r,0,1); }
                        """
                }
            });
            Assert.That(show.Error.Value, Is.Null);
            graph.Connect(show.In("histogram"), compute.Out("histogram"));
            graph.Connect(show.In("picture"), compute.Out("picture"));
            return show.Out("color");
        });

        ushort[] pixels = ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(graph), 1);

        Assert.That(Channel(pixels, 0), Is.EqualTo(0.75f).Within(1e-3f));
        Assert.That(Channel(pixels, 1), Is.EqualTo(0.25f).Within(1e-3f));
    }

    private const string TableCode = """
        #version 450
        layout(location=0) in vec2 uv;
        layout(location=0) out vec4 color;
        layout(set=0,binding=0) uniform sampler2D source;
        layout(set=0,binding=1) uniform sampler2D table;
        void main() { color=texture(source,uv)+texture(table,uv)*0.0; }
        """;

    [TestCase("image")]
    [TestCase("copy")]
    [TestCase("geometry")]
    public void WhatOnlyARenderCanRefuseStaysOnTheNodeThatMadeThePicture(string refused)
    {
        VulkanTestEnvironment.EnsureAvailable();
        if (refused == "geometry" && VulkanTestEnvironment.InvokeOnRenderThread(
                () => ShaderCapabilities.Query(GraphicsContextFactory.SharedContext!).GeometryShaders))
            Assert.Ignore("This device runs geometry stages.");

        using var files = new SceneHistoryHarness("shader-refused", duration: MediaTime.FromSeconds(1));
        var effect = new NodeGraphFilterEffect();
        GraphModel graph = effect.Model.Value!;
        var input = graph.Add(new FilterEffectInputNode());
        var pass = graph.Add(new ShaderFullscreenPassNode { Code = { Value = TableCode } });
        graph.Connect(pass.In("source"), input.Output);
        var output = graph.Add(new OutputNode());
        Connection result = graph.Connect(output.InputPort, pass.Out("color"));
        Populate(files, effect);
        files.AddElement(MediaTime.Zero, files.Scene.Duration.Value).AddObject(new RectShape
        {
            Width = { Value = 2 },
            Height = { Value = 2 },
            Fill = { Value = new SolidColorBrush(Colors.White) }
        });
        using var renderer = new SceneRenderer(files.Scene, RenderIntent.Preview, 1);
        ushort[] working = RenderScene(renderer, MediaTime.Zero, 1);

        Action restore;
        NodeMonitor<string?> error;
        void Replace(IOutputPort image)
        {
            graph.Disconnect(result);
            Connection replaced = graph.Connect(output.InputPort, image);
            restore = () =>
            {
                graph.Disconnect(replaced);
                graph.Connect(output.InputPort, pass.Out("color"));
            };
        }

        switch (refused)
        {
            case "image":
                var huge = graph.Add(new ShaderFixedImageNode());
                huge.Width.Property!.SetValue(1_000_000);
                Connection read = graph.Connect(pass.In("table"), huge.Output);
                restore = () => graph.Disconnect(read);
                error = pass.Error;
                break;
            case "copy":
                var destination = graph.Add(new ShaderImageNode());
                destination.Padding.Property!.SetValue(8f);
                var copy = graph.Add(new ShaderCopyImageNode());
                graph.Connect(copy.Source, pass.Out("color"));
                graph.Connect(copy.Destination, destination.Output);
                restore = null!;
                Replace(copy.Output);
                error = copy.Error;
                break;
            default:
                var stages = graph.Add(new ShaderExtendedRenderPassNode
                {
                    Vertex = { Value = StageVertexCode },
                    Fragment = { Value = StageFragmentCode },
                    Geometry = { Value = StageGeometryCode }
                });
                restore = null!;
                Replace(stages.Out("color"));
                error = stages.Error;
                break;
        }

        ushort[] failed = RenderScene(renderer, MediaTime.Zero, 1);
        Assert.That(error.Value, Is.Not.Null.And.Not.Empty);
        Assert.That(failed, Has.Some.Not.EqualTo((ushort)0), "The rest of the frame still renders.");
        Assert.That(failed, Is.Not.EqualTo(working), "A failure must not pass for a working result.");

        restore();
        Assert.That(RenderScene(renderer, MediaTime.Zero, 1), Is.EqualTo(working));
        Assert.That(pass.Error.Value, Is.Null);
    }

    private const string CutCode = """
        #version 450
        layout(location=0) in vec2 uv;
        layout(location=0) out vec4 color;
        layout(set=0,binding=0) uniform sampler2D source;
        layout(set=0,binding=1) uniform sampler2D matte;
        void main() { color=texture(source,uv)*texture(matte,uv).a; }
        """;

    [Test]
    public void APictureMadeFromAnEffectsInputCanBeReadBesideIt()
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-picture-of-input", duration: MediaTime.FromSeconds(1));
        var effect = new NodeGraphFilterEffect();
        GraphModel graph = effect.Model.Value!;
        var input = graph.Add(new FilterEffectInputNode());
        var matrix = graph.Add(new TranslateMatrixNode());
        matrix.X.Property!.SetValue(6f);
        var moved = graph.Add(new TransformNode());
        graph.Connect(moved.NamedInput("Input"), input.Output);
        graph.Connect(moved.Matrix, matrix.Items.OfType<IOutputPort>().Single());
        var pass = graph.Add(new ShaderFullscreenPassNode { Code = { Value = CutCode } });
        graph.Connect(pass.In("source"), input.Output);
        graph.Connect(pass.In("matte"), moved.Items.OfType<IOutputPort>().Single());
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        Populate(files, effect);
        using var renderer = new SceneRenderer(files.Scene, RenderIntent.Preview, 1);

        ushort[] pixels = RenderScene(renderer, MediaTime.Zero, 1);

        Assert.That(pass.Error.Value, Is.Null);
        const int FrameWidth = 48;
        const int Row = 20;
        float Alpha(int x) => Channel(pixels, (Row * FrameWidth + x) * 4 + 3);
        int[] covered = [.. Enumerable.Range(0, FrameWidth).Where(x => Alpha(x) > 0.5f)];
        Assert.That(covered, Has.Length.EqualTo(18), "A copy moved by 6 leaves 18 of the picture's 24 columns under both.");
    }

    [Test]
    public void AnEffectCoversWhatItWasGivenWhenItsCodeReadsAnotherPictureFirst()
    {
        VulkanTestEnvironment.EnsureAvailable();
        using var files = new SceneHistoryHarness("shader-effect-covers", duration: MediaTime.FromSeconds(1));
        var effect = new NodeGraphFilterEffect();
        GraphModel graph = effect.Model.Value!;
        var input = graph.Add(new FilterEffectInputNode());
        var geometry = graph.Add(new RectGeometryNode());
        ((IInputPort)geometry.Items.Single(item => item.Name.Value == "Width")).Property!.SetValue(4f);
        ((IInputPort)geometry.Items.Single(item => item.Name.Value == "Height")).Property!.SetValue(4f);
        var shape = graph.Add(new GeometryShapeNode());
        shape.Fill.Property!.SetValue(new SolidColorBrush(Colors.White));
        graph.Connect(shape.Geometry, geometry.OutputPort);
        var pass = graph.Add(new ShaderFullscreenPassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(location=0) in vec2 uv;
                    layout(location=0) out vec4 color;
                    layout(set=0,binding=0) uniform sampler2D small;
                    layout(set=0,binding=1) uniform sampler2D given;
                    void main() { color=texture(given,uv)+texture(small,uv)*0.0; }
                    """
            }
        });
        graph.Connect(pass.In("small"), shape.Output);
        graph.Connect(pass.In("given"), input.Output);
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        Populate(files, effect);
        using var renderer = new SceneRenderer(files.Scene, RenderIntent.Preview, 1);

        ushort[] pixels = RenderScene(renderer, MediaTime.Zero, 1);

        Assert.That(pass.Error.Value, Is.Null);
        int covered = Enumerable.Range(0, 48 * 40).Count(i => Channel(pixels, i * 4 + 3) > 0.5f);
        Assert.That(covered, Is.EqualTo(24 * 16), "The whole of what the effect was given.");
    }

    [Test]
    public void ANumberCanBeLoopStateAlongsideAnImage()
    {
        VulkanTestEnvironment.EnsureAvailable();
        const string ShowCode = """
            #version 450
            layout(location=0) out vec4 color;
            layout(set=0,binding=0) uniform sampler2D source;
            layout(set=0,binding=1) uniform Parameters { float amount; } parameters;
            void main() { color=vec4(texelFetch(source,ivec2(gl_FragCoord.xy),0).r,parameters.amount,0,1); }
            """;
        var graph = new GraphModel();
        var replay = graph.Add(new ShaderReplayNode());
        replay.StepsPerSecond.Property!.SetValue(5d);
        graph.Connect(replay.Time, NodeGraphTestHelpers.Source(graph, 1f));
        GraphGroup body = replay.Group.Value;
        ShaderLoopInfoNode info = body.Nodes.OfType<ShaderLoopInfoNode>().Single();
        Assert.That(body.Output.Value!.AddNodePort(info.Time, out _), Is.True);
        var inner = body.Add(new ShaderFullscreenPassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(location=0) out vec4 color;
                    layout(set=0,binding=0) uniform sampler2D source;
                    layout(set=0,binding=1) uniform Parameters { float amount; } parameters;
                    void main() { color=vec4(parameters.amount,0,0,1); }
                    """
            }
        });
        body.Connect(inner.In("source"), (IOutputPort)body.Input.Value!.Items[0]);
        body.Connect(inner.In("parameters.amount"), (IOutputPort)body.Input.Value.Items[1]);
        body.Connect((IInputPort)body.Output.Value.Items[0], inner.Out("color"));
        IOutputPort[] outputs = [.. replay.Items.OfType<IOutputPort>()];
        Assert.That(outputs, Has.Length.EqualTo(2));
        var after = graph.Add(new ShaderFullscreenPassNode { Code = { Value = ShowCode } });
        graph.Connect(after.In("source"), outputs[0]);
        graph.Connect(after.In("parameters.amount"), outputs[1]);
        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, after.Out("color"));

        foreach (GraphModel candidate in new[] { graph, Restored(graph) })
        {
            ushort[] pixels = ShaderNodeTestHelpers.Render(ShaderNodeTestHelpers.Record(candidate), 1);

            // The last step read what the step before it wrote, and the loop hands out what the last one wrote.
            Assert.That(Channel(pixels, 0), Is.EqualTo(0.8f).Within(1e-3f));
            Assert.That(Channel(pixels, 1), Is.EqualTo(1f).Within(1e-3f));
        }
    }
}
