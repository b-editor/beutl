using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;

namespace Beutl.UnitTests.NodeGraph;

/// <summary>What the wires record. None of this needs a GPU.</summary>
public class ShaderNodeRecordingTests
{
    private static (GraphModel Graph, OutputNode Output) NewGraph()
    {
        var graph = new GraphModel();
        return (graph, graph.Add(new OutputNode()));
    }

    private static ShaderGraphRenderNode Evaluate(GraphModel graph, GraphSnapshot snapshot)
    {
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);
        return ShaderNodeTestHelpers.Picture(graph, snapshot)!;
    }

    [Test]
    public void OnePassIsAllAFilterNeeds()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        graph.Connect(output.InputPort, pass.Out("color"));

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);

        ShaderPassDescription recorded = description.Passes.Single();
        Assert.That(recorded.Bindings.Single().Resource, Is.SameAs(description.Resources[0]),
            "A sampler nothing connects to reads the source image.");
        Assert.That(description.Output.Image, Is.SameAs(recorded.Attachments.Single().Image));
    }

    [Test]
    public void TheWiresDecideWhatRunsAndInWhichOrder()
    {
        var (graph, output) = NewGraph();
        var last = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "last" } });
        var first = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "first" } });
        var unused = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "unused" } });
        var mask = graph.Add(new ShaderComputePassNode { Name = { Value = "mask" } });
        last.Code.Value = """
            #version 450
            layout(location = 0) in vec2 uv;
            layout(location = 0) out vec4 color;
            layout(set = 0, binding = 0) uniform sampler2D source;
            layout(set = 0, binding = 1) uniform sampler2D mask;
            void main() { color = texture(source, uv) * texture(mask, uv); }
            """;
        graph.Connect(last.In("source"), first.Out("color"));
        graph.Connect(last.In("mask"), mask.Out("result"));
        graph.Connect(output.InputPort, last.Out("color"));

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);

        Assert.That(description.PassNames, Is.EqualTo(new[] { "first", "mask", "last" }));
        Assert.That(description.PassNames, Does.Not.Contain(unused.Name.Value));
    }

    [Test]
    public void ABranchReadByTwoPassesRunsOnce()
    {
        var (graph, output) = NewGraph();
        var shared = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "shared" } });
        var left = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "left" } });
        var right = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "right" } });
        var merge = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "merge" } });
        merge.Code.Value = """
            #version 450
            layout(location = 0) in vec2 uv;
            layout(location = 0) out vec4 color;
            layout(set = 0, binding = 0) uniform sampler2D a;
            layout(set = 0, binding = 1) uniform sampler2D b;
            void main() { color = texture(a, uv) + texture(b, uv); }
            """;
        graph.Connect(left.In("source"), shared.Out("color"));
        graph.Connect(right.In("source"), shared.Out("color"));
        graph.Connect(merge.In("a"), left.Out("color"));
        graph.Connect(merge.In("b"), right.Out("color"));
        graph.Connect(output.InputPort, merge.Out("color"));

        Assert.That(ShaderNodeTestHelpers.Record(graph).PassNames, Is.EqualTo(new[] { "shared", "left", "right", "merge" }));
    }

    [Test]
    public void AWriteToAConnectedImageComesOutAsTheSameImage()
    {
        var (graph, output) = NewGraph();
        var image = graph.Add(new ShaderImageNode());
        var first = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "first" } });
        graph.Connect(first.Targets, image.Output);
        var second = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "second" } });
        graph.Connect(second.Targets, first.Out("color"));
        var blend = graph.Add(new ShaderBlendNode());
        graph.Connect(second.Settings, blend.Output);
        graph.Connect(output.InputPort, second.Out("color"));

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);

        Assert.That(description.Passes[^1].Attachments.Single().Image, Is.SameAs(description.Passes[^2].Attachments.Single().Image));
        Assert.That(description.Passes[^1].Blend.Enabled, Is.True);
        Assert.That(description.Passes[^1].Attachments.Single().Load, Is.EqualTo(AttachmentLoadOp.Load), "Drawing onto a target keeps it.");
    }

    [Test]
    public void ANewOutputStartsClearAndAFreshTargetIsKnownToBeEmpty()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        Assert.That(ShaderNodeTestHelpers.Record(graph).Passes.Single().Attachments.Single().Load, Is.EqualTo(AttachmentLoadOp.Clear));

        var image = graph.Add(new ShaderImageNode());
        graph.Connect(pass.Targets, image.Output);
        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);
        Assert.That(description.Passes[0].Transfer!.Kind, Is.EqualTo(ShaderTransferKind.ClearImage));
        Assert.That(description.Passes[^1].Attachments.Single().Load, Is.EqualTo(AttachmentLoadOp.Load));

        var load = graph.Add(new ShaderLoadNode());
        load.Load.Property!.SetValue(AttachmentLoadOp.DontCare);
        graph.Connect(pass.Settings, load.Output);
        Assert.That(ShaderNodeTestHelpers.Record(graph).Passes[^1].Attachments.Single().Load, Is.EqualTo(AttachmentLoadOp.DontCare));
    }

    [Test]
    public void AnOutputWithoutATargetIsClearedWhenItsPartnerKeepsOne()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        pass.Code.Value = """
            #version 450
            layout(location = 0) out vec4 first;
            layout(location = 1) out vec4 second;
            void main() { first = vec4(1); second = vec4(1); }
            """;
        var image = graph.Add(new ShaderImageNode());
        graph.Connect(pass.Targets, image.Output);
        graph.Connect(output.InputPort, pass.Out("second"));

        ShaderPassDescription recorded = ShaderNodeTestHelpers.Record(graph).Passes[^1];

        Assert.That(recorded.Attachments.Select(a => a.Load), Is.All.EqualTo(AttachmentLoadOp.Load), "A pass's attachments share one load.");
    }

    [Test]
    public void AGraphWithNoInputConnectedStillRecordsForTheFrame()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        pass.Code.Value = """
            #version 450
            layout(location = 0) in vec2 uv;
            layout(location = 0) out vec4 color;
            void main() { color = vec4(uv, 0, 1); }
            """;
        graph.Connect(output.InputPort, pass.Out("color"));
        using var snapshot = new GraphSnapshot();

        ShaderGraphRenderNode picture = Evaluate(graph, snapshot);

        Assert.That(pass.Error.Value, Is.Null);
        Assert.That(picture.Description!.PassNames, Has.Length.EqualTo(1));
    }

    [Test]
    public void AnImageConnectedToAWriteOnlyBindingIsOnlyWritten()
    {
        var (graph, output) = NewGraph();
        var compute = graph.Add(new ShaderComputePassNode());
        var image = graph.Add(new ShaderImageNode());
        graph.Connect(compute.In("result"), image.Output);
        graph.Connect(output.InputPort, compute.Out("result"));

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);

        ShaderPassDescription pass = description.Passes[^1];
        Assert.That(pass.Bindings.Single(b => b.Binding == 1).Access, Is.EqualTo(ResourceAccess.Write));
        Assert.That(pass.WorkImage, Is.SameAs(pass.Bindings.Single(b => b.Binding == 1).Resource),
            "With no dispatch set, a compute pass covers what it writes.");
    }

    [Test]
    public void AnOutputWithIntegerComponentsGetsAnIntegerImage()
    {
        var (graph, output) = NewGraph();
        var data = graph.Add(new ShaderFullscreenPassNode());
        data.Code.Value = """
            #version 450
            layout(location = 0) out uvec4 ids;
            void main() { ids = uvec4(1); }
            """;
        var view = graph.Add(new ShaderFullscreenPassNode());
        view.Code.Value = """
            #version 450
            layout(location = 0) in vec2 uv;
            layout(location = 0) out vec4 color;
            layout(set = 0, binding = 0) uniform usampler2D ids;
            void main() { color = vec4(texture(ids, uv)); }
            """;
        var nearest = graph.Add(new ShaderSamplerNode());
        nearest.MinFilter.Property!.SetValue(SamplerFilter.Nearest);
        nearest.MagFilter.Property!.SetValue(SamplerFilter.Nearest);
        nearest.MipmapFilter.Property!.SetValue(SamplerFilter.Nearest);
        graph.Connect(nearest.Image, data.Out("ids"));
        graph.Connect(view.In("ids"), nearest.Output);
        graph.Connect(output.InputPort, view.Out("color"));

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);

        Assert.That(((ShaderImage)description.Passes[0].Attachments[0].Image).Format, Is.EqualTo(TextureFormat.RGBA32UInt));
        Assert.That(description.Passes[1].Bindings.Single().Sampler.MinFilter, Is.EqualTo(SamplerFilter.Nearest));
    }

    [Test]
    public void AnOutputFollowsWhatThePassReadsUnlessPaddedOrScaled()
    {
        var (graph, output) = NewGraph();
        var same = graph.Add(new ShaderFullscreenPassNode());
        var half = graph.Add(new ShaderFullscreenPassNode());
        half.ResolutionScale.Property!.SetValue(0.5f);
        graph.Connect(half.In("source"), same.Out("color"));
        var wide = graph.Add(new ShaderFullscreenPassNode());
        wide.Padding.Property!.SetValue(8f);
        graph.Connect(wide.In("source"), half.Out("color"));
        graph.Connect(output.InputPort, wide.Out("color"));

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);

        ShaderImage[] images = [.. description.Passes.Select(pass => pass.Attachments[0].Image)];
        Rect source = new(10, 20, 100, 50);
        Assert.That(images[0].RasterSource, Is.SameAs(description.Resources[0]));
        Assert.That(images[1].ResolutionScale, Is.EqualTo(0.5f));
        Assert.That(images[2].ResolveBounds(source), Is.EqualTo(source.Inflate(8)));
        Assert.That(description.TransformBounds(source), Is.EqualTo(source.Inflate(8)));
    }

    [Test]
    public void OnlyAnInputThatCoversSomethingElseIsCopiedToLineUp()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "pass" } });
        graph.Connect(output.InputPort, pass.Out("color"));
        Assert.That(ShaderNodeTestHelpers.Record(graph).PassNames, Is.EqualTo(new[] { "pass" }));

        pass.Padding.Property!.SetValue(4f);
        Assert.That(ShaderNodeTestHelpers.Record(graph).PassNames, Is.EqualTo(new[] { "pass.source.align", "pass" }));

        // A different density is the same place.
        pass.Padding.Property!.SetValue(0f);
        pass.ResolutionScale.Property!.SetValue(0.5f);
        Assert.That(ShaderNodeTestHelpers.Record(graph).PassNames, Is.EqualTo(new[] { "pass" }));

        // A fixed-size image has no place to keep.
        var image = graph.Add(new ShaderFixedImageNode());
        graph.Connect(pass.In("source"), image.Output);
        Assert.That(ShaderNodeTestHelpers.Record(graph).PassNames.Where(name => name.EndsWith(".align")), Is.Empty);
    }

    [Test]
    public void PartialRenderingAndAnInputRegionReachThePass()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        var partial = graph.Add(new ShaderPartialRenderingNode());
        var region = graph.Add(new ShaderInputRegionNode());
        region.Padding.Property!.SetValue(3f);
        graph.Connect(pass.In("source"), region.Output);
        graph.Connect(pass.Settings, partial.Output);
        graph.Connect(output.InputPort, pass.Out("color"));

        ShaderPassDescription recorded = ShaderNodeTestHelpers.Record(graph).Passes.Single();

        Assert.That(recorded.PartialRendering, Is.True);
        Assert.That(recorded.InputRegions[0].Map(new Rect(10, 10, 4, 4)), Is.EqualTo(new Rect(7, 7, 10, 10)));
    }

    [Test]
    public void HalfATessellationPairReportsOnItsNode()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderExtendedRenderPassNode());
        graph.Connect(output.InputPort, pass.Out("color"));

        pass.TessellationControl.Value = """
            #version 450
            layout(vertices = 3) out;
            void main() { gl_TessLevelOuter[0] = 1; }
            """;

        Assert.That(pass.Error.Value, Does.Contain("together"));
        using var snapshot = new GraphSnapshot();
        Evaluate(graph, snapshot);
        Assert.That(pass.Error.Value, Does.Contain("together"));
        Assert.That(ShaderNodeTestHelpers.Picture(graph, snapshot)!.Description, Is.Null);
    }

    [Test]
    public void AFixedSizeInputDoesNotDecideWhatAPassCovers()
    {
        var (graph, output) = NewGraph();
        var lookup = graph.Add(new ShaderFixedImageNode());
        var pass = graph.Add(new ShaderFullscreenPassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(location = 0) in vec2 uv;
                    layout(location = 0) out vec4 color;
                    layout(set = 0, binding = 0) uniform sampler2D lookup;
                    layout(set = 0, binding = 1) uniform sampler2D source;
                    void main() { color = texture(lookup, texture(source, uv).rg); }
                    """
            }
        });
        graph.Connect(pass.In("lookup"), lookup.Output);
        graph.Connect(output.InputPort, pass.Out("color"));

        var bounds = new Rect(10, 20, 640, 360);
        Assert.That(ShaderNodeTestHelpers.Record(graph).TransformBounds(bounds), Is.EqualTo(bounds));
    }

    [Test]
    public void APaddingNoRectangleCanHoldReportsOnTheApplyNode()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        pass.Padding.Property!.SetValue(float.MaxValue);
        graph.Connect(output.InputPort, pass.Out("color"));

        using var snapshot = new GraphSnapshot();
        Evaluate(graph, snapshot);

        Assert.That(pass.Error.Value, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void AnUnconnectedArrayBlockKeepsItsElementStride()
    {
        var (graph, output) = NewGraph();
        var compute = graph.Add(new ShaderComputePassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(local_size_x = 1) in;
                    layout(set = 0, binding = 0, std430) writeonly buffer Bins { uint bins[256]; } histogram;
                    layout(set = 0, binding = 1, rgba16f) uniform writeonly image2D picture;
                    void main() { histogram.bins[0] = 1u; imageStore(picture, ivec2(0), vec4(1)); }
                    """
            }
        });
        graph.Connect(output.InputPort, compute.Out("picture"));

        using var snapshot = new GraphSnapshot();
        Evaluate(graph, snapshot);
        Assert.That(compute.Error.Value, Is.Null);

        ShaderBuffer bins = ShaderNodeTestHelpers.Record(graph).Resources.OfType<ShaderBuffer>().Single();
        Assert.That((bins.Count, bins.Stride), Is.EqualTo((256, 4)));
    }

    [Test]
    public void AMeshKeepsItsOwnTextureCoordinatesThroughPadding()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderRenderPassNode
        {
            Name = { Value = "mesh" },
            Fragment =
            {
                Value = """
                    #version 450
                    layout(location = 0) out vec4 color;
                    layout(set = 0, binding = 0) uniform sampler2D source;
                    void main() { color = texture(source, gl_FragCoord.xy); }
                    """
            }
        });
        pass.Padding.Property!.SetValue(4f);
        graph.Connect(output.InputPort, pass.Out("color"));

        Assert.That(ShaderNodeTestHelpers.Record(graph).PassNames, Is.EqualTo(new[] { "mesh" }));
    }

    [Test]
    public void APassToldWhereItsImagesLieDoesItsOwnArithmetic()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode
        {
            Name = { Value = "manual" },
            Code =
            {
                Value = """
                    #version 450
                    layout(location = 0) out vec4 color;
                    layout(set = 0, binding = 0) uniform sampler2D source;
                    layout(set = 0, binding = 1) uniform Frame { vec4 logical; vec4 raster; vec4 size; } frame;
                    void main() { color = texture(source, gl_FragCoord.xy / frame.size.xy); }
                    """
            }
        });
        pass.Padding.Property!.SetValue(4f);
        graph.Connect(output.InputPort, pass.Out("color"));
        Assert.That(ShaderNodeTestHelpers.Record(graph).PassNames, Is.EqualTo(new[] { "manual.source.align", "manual" }));

        var parameters = graph.Add(new ShaderImageParametersNode());
        graph.Connect(pass.In("frame"), parameters.Output);

        Assert.That(ShaderNodeTestHelpers.Record(graph).PassNames, Is.EqualTo(new[] { "manual" }));
    }

    [Test]
    public void ImagesThatEachTakeAfterTheLastStayOneStepFromWhatTheyShare()
    {
        ShaderGraphDescription description = ShaderGraphBuilder.Build(0, static (builder, _) =>
        {
            ShaderImage padded = builder.Image2D("padded", builder.Source, padding: 2);
            ShaderImage first = builder.Image2D("first", padded, padding: 3);
            ShaderImage second = builder.Image2D("second", first);
            first.RasterSource = padded;
            second.RasterSource = first;
            Assert.That(second.Like, Is.SameAs(builder.Source));
            Assert.That(second.Padding, Is.EqualTo(5));
            Assert.That(second.RasterSource, Is.SameAs(padded));
            Assert.That(second.RasterRoot, Is.SameAs(padded));
            var bounds = new Rect(0, 0, 10, 10);
            Assert.That(second.ResolveBounds(bounds), Is.EqualTo(bounds.Inflate(5)));

            // What changes the bounds on its own account stays in the way.
            ShaderImage moved = builder.Image2D("moved", builder.Source);
            moved.BoundsTransforms = [Matrix.CreateScale(2, 2)];
            ShaderImage beyond = builder.Image2D("beyond", moved);
            Assert.That(beyond.Like, Is.SameAs(moved));
            ShaderImage placed = builder.Image2D("placed", new Rect(1, 2, 3, 4));
            Assert.That(builder.Image2D("after", placed, padding: 1).ResolveBounds(bounds), Is.EqualTo(new Rect(0, 1, 5, 6)));
            return builder.Output(builder.Source);
        });
        Assert.That(description, Is.Not.Null);
    }

    [Test]
    public void ARepeatRecordsItsBodyOncePerIterationUnderNamesThatDoNotCollide()
    {
        var (graph, output) = NewGraph();
        var repeat = graph.Add(new ShaderRepeatNode());
        repeat.Count.Property!.SetValue(3);
        var body = repeat.Group.Value;
        var pass = body.Add(new ShaderFullscreenPassNode { Name = { Value = "step" } });
        body.Connect(pass.In("source"), (IOutputPort)body.Input.Value!.Items[0]);
        body.Connect((IInputPort)body.Output.Value!.Items[0], pass.Out("color"));
        graph.Connect(output.InputPort, repeat.Items.OfType<IOutputPort>().First());

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);

        Assert.That(description.PassNames, Is.EqualTo(new[] { "step", "step#1", "step#2" }));
        Assert.That(description.Passes[1].Bindings.Single().Resource, Is.SameAs(description.Passes[0].Attachments[0].Image));
        Assert.That(description.Passes[2].Bindings.Single().Resource, Is.SameAs(description.Passes[1].Attachments[0].Image));
    }

    [Test]
    public void AReplayDeepEnoughToOverflowARecursiveRecordingStillRecords()
    {
        var (graph, output) = NewGraph();
        var replay = graph.Add(new ShaderReplayNode());
        replay.StepsPerSecond.Property!.SetValue(50d);
        var body = replay.Group.Value;
        var pass = body.Add(new ShaderFullscreenPassNode());
        body.Connect(pass.In("source"), (IOutputPort)body.Input.Value!.Items[0]);
        body.Connect((IInputPort)body.Output.Value!.Items[0], pass.Out("color"));
        graph.Connect(output.InputPort, replay.Items.OfType<IOutputPort>().First());
        graph.Connect(replay.Time, NodeGraphTestHelpers.Source(graph, 1000f));

        ShaderGraphDescription description = ShaderNodeTestHelpers.Record(graph);

        Assert.That(description.PassNames.Length, Is.EqualTo(50000));

        // What a render asks before anything runs: every image takes after the one before it.
        var bounds = new Rect(0, 0, 32, 24);
        Assert.That(description.TransformBounds(bounds), Is.EqualTo(bounds));
        Assert.That(description.ClampWorkingScale(bounds, 1, default), Is.EqualTo(1f));
    }

    [Test]
    public void AStateNothingWritesKeepsItsValueThroughTheLoop()
    {
        var (graph, output) = NewGraph();
        var repeat = graph.Add(new ShaderRepeatNode());
        repeat.Count.Property!.SetValue(5);
        var first = graph.Add(new ShaderFullscreenPassNode { Name = { Value = "before" } });
        graph.Connect(repeat.Items.OfType<IInputPort>().First(), first.Out("color"));
        graph.Connect(output.InputPort, repeat.Items.OfType<IOutputPort>().First());

        Assert.That(ShaderNodeTestHelpers.Record(graph).PassNames, Is.EqualTo(new[] { "before" }));
    }

    [Test]
    public void AnImpossibleReplayReportsOnItsNodeAndLeavesTheState()
    {
        var (graph, output) = NewGraph();
        var replay = graph.Add(new ShaderReplayNode());
        replay.StepsPerSecond.Property!.SetValue(1000000d);
        graph.Connect(replay.Time, NodeGraphTestHelpers.Source(graph, 100000f));
        graph.Connect(output.InputPort, replay.Items.OfType<IOutputPort>().First());
        using var snapshot = new GraphSnapshot();

        Evaluate(graph, snapshot);

        Assert.That(replay.Error.Value, Is.Not.Null.And.Not.Empty);

        replay.StepsPerSecond.Property!.SetValue(-1d);
        Evaluate(graph, snapshot);
        Assert.That(replay.Error.Value, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(100d, false)]
    [TestCase(100.001d, true)]
    public void AReplayIsLimitedByTheStepsItWouldRun(double time, bool tooMany)
    {
        var (graph, output) = NewGraph();
        var replay = graph.Add(new ShaderReplayNode());
        replay.StepsPerSecond.Property!.SetValue(1000d);
        graph.Connect(replay.Time, NodeGraphTestHelpers.Source(graph, time));
        graph.Connect(output.InputPort, replay.Items.OfType<IOutputPort>().First());
        using var snapshot = new GraphSnapshot();

        Evaluate(graph, snapshot);

        Assert.That(replay.Error.Value, tooMany ? Is.Not.Null.And.Not.Empty : Is.Null);
    }

    [Test]
    public void PaddingGrowsAnImageOfExplicitBoundsAndMustNotBeNegative()
    {
        var (graph, output) = NewGraph();
        var image = graph.Add(new ShaderImageNode());
        image.Bounds.Property!.SetValue(new Rect(0, 0, 10, 20));
        image.Padding.Property!.SetValue(2f);
        var pass = graph.Add(new ShaderFullscreenPassNode());
        graph.Connect(pass.Targets, image.Output);
        graph.Connect(output.InputPort, pass.Out("color"));

        Assert.That(ShaderNodeTestHelpers.Record(graph).TransformBounds(new Rect(0, 0, 1, 1)), Is.EqualTo(new Rect(-2, -2, 14, 24)));

        image.Padding.Property!.SetValue(-1f);
        using var snapshot = new GraphSnapshot();
        Assert.That(Evaluate(graph, snapshot).Description, Is.Null);
    }

    [Test]
    public void ABufferOfPartWordsStillStartsWithItsContentsKnown()
    {
        var (graph, output) = NewGraph();
        var buffer = graph.Add(new ShaderBufferNode());
        buffer.Count.Property!.SetValue(3);
        buffer.Stride.Property!.SetValue(2);
        var pass = graph.Add(new ShaderFullscreenPassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(location = 0) out vec4 color;
                    layout(set = 0, binding = 0, std430) readonly buffer Data { uint first; } data;
                    void main() { color = vec4(float(data.first)); }
                    """
            }
        });
        graph.Connect(pass.In("data"), buffer.Output);
        graph.Connect(output.InputPort, pass.Out("color"));

        ShaderNodeTestHelpers.Record(graph);

        Assert.That(pass.Error.Value, Is.Null);
    }

    [TestCase(100000001)]
    [TestCase(100000002)]
    public void ABufferTooLargeForAGraphIsRefusedWithoutBeingAllocated(int count)
    {
        var (graph, output) = NewGraph();
        var buffer = graph.Add(new ShaderBufferNode());
        buffer.Count.Property!.SetValue(count);
        buffer.Stride.Property!.SetValue(6);
        var pass = graph.Add(new ShaderFullscreenPassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(location = 0) out vec4 color;
                    layout(set = 0, binding = 0, std430) readonly buffer Data { uint first; } data;
                    void main() { color = vec4(float(data.first)); }
                    """
            }
        });
        graph.Connect(pass.In("data"), buffer.Output);
        graph.Connect(output.InputPort, pass.Out("color"));
        using var snapshot = new GraphSnapshot();

        long before = GC.GetAllocatedBytesForCurrentThread();
        ShaderGraphRenderNode picture = Evaluate(graph, snapshot);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(allocated, Is.LessThan(64L * 1024 * 1024));
        Assert.That(picture.Description, Is.Null);
        Assert.That(pass.Error.Value, Does.Contain(((long)count * 6).ToString()));
    }

    [TestCase(1e-12d)]
    [TestCase(double.Epsilon)]
    public void AStepTooLongForTheClockReportsOnTheReplay(double stepsPerSecond)
    {
        var (graph, output) = NewGraph();
        var replay = graph.Add(new ShaderReplayNode());
        replay.StepsPerSecond.Property!.SetValue(stepsPerSecond);
        graph.Connect(output.InputPort, replay.Items.OfType<IOutputPort>().First());
        using var snapshot = new GraphSnapshot();

        Evaluate(graph, snapshot);

        Assert.That(replay.Error.Value, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ATimeThatIsNotANumberReportsOnTheReplay()
    {
        var (graph, output) = NewGraph();
        var replay = graph.Add(new ShaderReplayNode());
        graph.Connect(replay.Time, NodeGraphTestHelpers.Source(graph, "soon"));
        graph.Connect(output.InputPort, replay.Items.OfType<IOutputPort>().First());
        using var snapshot = new GraphSnapshot();

        Evaluate(graph, snapshot);

        Assert.That(replay.Error.Value, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void StepsTooShortForTheClockToTellApartReportOnTheReplay()
    {
        var (graph, output) = NewGraph();
        var replay = graph.Add(new ShaderReplayNode());
        replay.StepsPerSecond.Property!.SetValue(1e9d);
        graph.Connect(output.InputPort, replay.Items.OfType<IOutputPort>().First());
        using var snapshot = new GraphSnapshot();

        Evaluate(graph, snapshot);

        Assert.That(replay.Error.Value, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void ARepeatTooLongToRunReportsOnItsNodeUntilItIsShorter()
    {
        var (graph, output) = NewGraph();
        var repeat = graph.Add(new ShaderRepeatNode());
        repeat.Count.Property!.SetValue(100_001);
        graph.Connect(output.InputPort, repeat.Items.OfType<IOutputPort>().First());
        using var snapshot = new GraphSnapshot();

        Evaluate(graph, snapshot);
        Assert.That(repeat.Error.Value, Is.Not.Null.And.Not.Empty);

        repeat.Count.Property!.SetValue(1);
        Evaluate(graph, snapshot);
        Assert.That(repeat.Error.Value, Is.Null);
    }

    private static object? OutputOf(GraphModel graph, GraphNode node, IOutputPort port)
    {
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);
        return snapshot.GetItemValue(snapshot.FindSlotIndex(node), node.Items.IndexOf(port))!.GetBoxed();
    }

    [TestCase(ShaderDataType.Int32, "-2", new byte[] { 0xFE, 0xFF, 0xFF, 0xFF })]
    [TestCase(ShaderDataType.UInt32, "4294967295", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    [TestCase(ShaderDataType.Int64, "-2", new byte[] { 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    [TestCase(ShaderDataType.UInt64, "18446744073709551615", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF })]
    [TestCase(ShaderDataType.Float64, "0.5", new byte[] { 0, 0, 0, 0, 0, 0, 0xE0, 0x3F })]
    public void DataIsWrittenAsItsTypeSays(ShaderDataType type, string text, byte[] expected)
    {
        var graph = new GraphModel();
        var data = graph.Add(new ShaderDataNode());
        data.DataType.Property!.SetValue(type);
        data.Text.Property!.SetValue(text);

        Assert.That(OutputOf(graph, data, data.Output), Is.EqualTo(expected));
        Assert.That(data.Error.Value, Is.Null);
    }

    [Test]
    public void DataThatDoesNotParseSaysSoOnItsNode()
    {
        var graph = new GraphModel();
        var data = graph.Add(new ShaderDataNode());
        data.Text.Property!.SetValue("one 2");

        Assert.That(OutputOf(graph, data, data.Output), Is.Empty);
        Assert.That(data.Error.Value, Is.Not.Null.And.Not.Empty);

        data.Text.Property!.SetValue("1 2");
        Assert.That(OutputOf(graph, data, data.Output), Has.Length.EqualTo(8));
        Assert.That(data.Error.Value, Is.Null);
    }

    [Test]
    public void PackWritesEachValueAtItsOwnSize()
    {
        var graph = new GraphModel();
        var pack = graph.Add(new ShaderPackNode());
        graph.Connect(pack.Values, NodeGraphTestHelpers.Source(graph, true));
        graph.Connect(pack.Values, NodeGraphTestHelpers.Source(graph, -2));
        graph.Connect(pack.Values, NodeGraphTestHelpers.Source(graph, 3u));
        graph.Connect(pack.Values, NodeGraphTestHelpers.Source(graph, -4L));
        graph.Connect(pack.Values, NodeGraphTestHelpers.Source(graph, 5UL));
        graph.Connect(pack.Values, NodeGraphTestHelpers.Source(graph, 0.5d));
        graph.Connect(pack.Values, NodeGraphTestHelpers.Source(graph, new Vector3(1, 2, 3)));
        graph.Connect(pack.Values, NodeGraphTestHelpers.Source(graph, new Vector4(4, 5, 6, 7)));
        using var expected = new MemoryStream();
        using (var writer = new BinaryWriter(expected, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            writer.Write(-2);
            writer.Write(3u);
            writer.Write(-4L);
            writer.Write(5UL);
            writer.Write(0.5d);
            foreach (float value in new float[] { 1, 2, 3, 4, 5, 6, 7 }) writer.Write(value);
        }

        Assert.That(OutputOf(graph, pack, pack.Output), Is.EqualTo(expected.ToArray()));
    }

    [Test]
    public void AnUnsizedStorageBufferWithNothingConnectedSaysSo()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderComputePassNode());
        pass.Code.Value = """
            #version 450
            layout(local_size_x = 1) in;
            layout(set = 0, binding = 0, rgba16f) uniform writeonly image2D result;
            layout(set = 0, binding = 1, std430) buffer Data { float values[]; } data;
            void main() { }
            """;
        graph.Connect(output.InputPort, pass.Out("result"));
        using var snapshot = new GraphSnapshot();

        Evaluate(graph, snapshot);

        Assert.That(pass.Error.Value, Does.Contain("data"));
    }

    [Test]
    public void CodeThatDoesNotCompileReportsWithoutBreakingTheEvaluationAndThenRecovers()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        graph.Connect(output.InputPort, pass.Out("color"));
        using var snapshot = new GraphSnapshot();
        Assert.That(Evaluate(graph, snapshot).Description, Is.Not.Null);

        pass.Code.Value = "broken";
        Assert.That(Evaluate(graph, snapshot).Description, Is.Null);
        Assert.That(pass.Error.Value, Is.Not.Null.And.Not.Empty);

        pass.Code.Value = ShaderFullscreenPassNode.DefaultCode;
        Assert.That(Evaluate(graph, snapshot).Description, Is.Not.Null);
        Assert.That(pass.Error.Value, Is.Null);
    }

    [Test]
    public void AMistakeOnlyTheRendererWouldFindIsCaughtBeforeItGetsThere()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        var image = graph.Add(new ShaderFixedImageNode());
        image.Kind.Property!.SetValue(ImageKind.Image3D);
        graph.Connect(pass.In("source"), image.Output);
        graph.Connect(output.InputPort, pass.Out("color"));
        using var snapshot = new GraphSnapshot();

        Evaluate(graph, snapshot);

        Assert.That(pass.Error.Value, Does.Contain("binding 0"));
    }

    [Test]
    public void TheSameGraphAgainKeepsTheRecordedDescription()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        pass.Code.Value = """
            #version 450
            layout(location = 0) in vec2 uv;
            layout(location = 0) out vec4 color;
            layout(set = 0, binding = 0) uniform sampler2D source;
            layout(set = 0, binding = 1) uniform Parameters { float amount; } parameters;
            void main() { color = texture(source, uv) * parameters.amount; }
            """;
        graph.Connect(output.InputPort, pass.Out("color"));
        using var snapshot = new GraphSnapshot();

        ShaderGraphRenderNode picture = Evaluate(graph, snapshot);
        ShaderGraphDescription? first = picture.Description;
        Assert.That(first, Is.Not.Null);
        Assert.That(Evaluate(graph, snapshot), Is.SameAs(picture), "A picture stays the same render node.");
        Assert.That(picture.Description, Is.SameAs(first), "Recording anew each frame must not look like a change.");

        pass.Set("parameters.amount", 0.5f);
        Evaluate(graph, snapshot);
        Assert.That(picture.Description, Is.Not.SameAs(first));
    }

    [Test]
    public void AnOutputReadByTwoPicturesIsOnePictureAndBothWiresShowTheConversion()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        var transform = graph.Add(new TransformNode());
        Connection direct = graph.Connect(output.InputPort, pass.Out("color"));
        Connection listed = graph.Connect((IInputPort)transform.NamedInput("Input"), pass.Out("color"));
        using var snapshot = new GraphSnapshot();

        ShaderGraphRenderNode picture = Evaluate(graph, snapshot);

        Assert.That(direct.Status.Value, Is.EqualTo(ConnectionStatus.Convert));
        Assert.That(listed.Status.Value, Is.EqualTo(ConnectionStatus.Convert));
        var transformed = (ContainerRenderNode)snapshot.GetItemValue(snapshot.FindSlotIndex(transform),
            transform.Items.IndexOf(transform.Items.OfType<IOutputPort>().Single()))!.GetBoxed()!;
        Assert.That(((ReferencesChildRenderNode)transformed.Children.Single()).Child, Is.SameAs(picture));
    }

    [Test]
    public void AFailureTurnsTheWireRedAndAFixTurnsItBack()
    {
        var (graph, output) = NewGraph();
        var pass = graph.Add(new ShaderFullscreenPassNode());
        Connection wire = graph.Connect(output.InputPort, pass.Out("color"));
        using var snapshot = new GraphSnapshot();

        pass.Code.Value = "broken";
        Evaluate(graph, snapshot);
        Assert.That(wire.Status.Value, Is.EqualTo(ConnectionStatus.Error));

        pass.Code.Value = ShaderFullscreenPassNode.DefaultCode;
        Evaluate(graph, snapshot);
        Assert.That(wire.Status.Value, Is.EqualTo(ConnectionStatus.Convert));
    }

    [Test]
    public void APictureOnAnImageInputIsRecordedAsWhatItsPassReads()
    {
        var (graph, output) = NewGraph();
        var input = graph.Add(new FilterEffectInputNode());
        var pass = graph.Add(new ShaderFullscreenPassNode());
        Connection wire = graph.Connect(pass.In("source"), input.Output);
        graph.Connect(output.InputPort, pass.Out("color"));
        using var snapshot = new GraphSnapshot();

        ShaderGraphRenderNode picture = Evaluate(graph, snapshot);

        Assert.That(wire.Status.Value, Is.EqualTo(ConnectionStatus.Convert));
        Assert.That(picture.Description!.ExternalImageCount, Is.Zero, "The only picture is the source.");
        Assert.That(picture.Children, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnEffectsInputIsTheSourceWhicheverPictureItsCodeReadsFirst()
    {
        var (graph, output) = NewGraph();
        var input = graph.Add(new FilterEffectInputNode());
        var pass = graph.Add(new ShaderFullscreenPassNode
        {
            Code =
            {
                Value = """
                    #version 450
                    layout(location = 0) in vec2 uv;
                    layout(location = 0) out vec4 color;
                    layout(set = 0, binding = 0) uniform sampler2D first;
                    layout(set = 0, binding = 1) uniform sampler2D second;
                    void main() { color = texture(first, uv) + texture(second, uv); }
                    """
            }
        });
        graph.Connect(pass.In("first"), graph.Add(new TransformNode()).Items.OfType<IOutputPort>().Single());
        graph.Connect(pass.In("second"), input.Output);
        graph.Connect(output.InputPort, pass.Out("color"));
        using var snapshot = new GraphSnapshot();

        ShaderGraphRenderNode picture = Evaluate(graph, snapshot);

        Assert.That(picture.Description!.ExternalImageCount, Is.EqualTo(1));
        Assert.That(((ReferencesChildRenderNode)((ContainerRenderNode)picture.Children[0]).Children[0]).Child,
            Is.TypeOf<FilterEffectInputRenderNode>());
    }
}
