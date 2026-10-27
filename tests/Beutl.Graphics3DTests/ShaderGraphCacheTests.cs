using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using static Beutl.Graphics3DTests.ShaderGraphContractTests;
using static Beutl.Graphics3DTests.ShaderGraphOptimizationTests;
using static Beutl.Graphics3DTests.ShaderGraphResourceTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphCacheTests
{
    [Test]
    public void ResultCacheTracksSpecializationChangesAndReusesPreviousPipelines()
    {
        OnDevice(_ =>
        {
            Rect bounds = new(0, 0, 64, 32);
            using var brush = (Brush.Resource)new SolidColorBrush(Colors.White).ToResource(CompositionContext.Default);
            using var rectangle = new RectangleRenderNode(bounds, brush, null);
            using var node = new GraphNode(rectangle, ShaderGraphSpecializationTests.Color(0.25f));
            using var renderer = new RenderNodeRenderer(node, new()
            {
                Intent = RenderIntent.Preview,
                Purpose = RenderRequestPurpose.Frame,
                TargetDomain = bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = RenderCacheOptions.Enabled
            });
            WarmCache(renderer);
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(RenderRed(renderer), Is.EqualTo(0.25f));
            node.SetGraph(ShaderGraphSpecializationTests.Color(0.75f));
            Assert.That(RenderRed(renderer), Is.EqualTo(0.75f));
            long creations = renderer.ProgramCacheStatistics.Creations;
            node.SetGraph(ShaderGraphSpecializationTests.Color(0.25f));
            Assert.That(RenderRed(renderer), Is.EqualTo(0.25f));
            Assert.That(renderer.ProgramCacheStatistics.Creations, Is.EqualTo(creations));
        });
    }

    [Test]
    public void ResultCacheTracksTimeResetAndColorWhileProgramsRemainReusable()
    {
        OnDevice(_ =>
        {
            Rect bounds = new(0, 0, 64, 32);
            using var brush = (Brush.Resource)new SolidColorBrush(Colors.White).ToResource(CompositionContext.Default);
            using var rectangle = new RectangleRenderNode(bounds, brush, null);
            using var node = new GraphNode(rectangle, History(0.75));
            using var renderer = new RenderNodeRenderer(node, new()
            {
                Intent = RenderIntent.Preview,
                Purpose = RenderRequestPurpose.Frame,
                TargetDomain = bounds,
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = RenderCacheOptions.Enabled,
            });
            WarmCache(renderer);
            Assert.That(RenderRed(renderer), Is.EqualTo(0.75f).Within(0.002));
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(RenderRed(renderer), Is.EqualTo(0.75f).Within(0.002));
            Assert.That(renderer.LastExecutionStatistics.ShaderStageExecutions, Is.Zero);

            node.SetGraph(History(0.25));
            Assert.That(RenderRed(renderer), Is.EqualTo(0.25f).Within(0.002));
            node.SetGraph(History(0.75, 0.5));
            Assert.That(RenderRed(renderer), Is.EqualTo(0.25f).Within(0.002));
            Assert.That(renderer.ProgramCacheStatistics.Creations, Is.EqualTo(3));

            static ShaderGraphDescription Color(ColorEncoding encoding) => ShaderGraphBuilder.Build(encoding, static (graph, color) =>
            {
                ShaderImage output = graph.Image2D("encoded", graph.Source);
                graph.Fullscreen("paint", ShaderGraphTests.ColorProgram, pass =>
                {
                    pass.PushConstants(new System.Numerics.Vector4(0.5f, 0, 0, 1));
                    pass.ColorAttachment(0, output);
                });
                return graph.Output(output, color);
            });
            ShaderGraphDescription linear = Color(ColorEncoding.LinearStraight);
            ShaderGraphDescription srgb = Color(ColorEncoding.SrgbStraight);
            Assert.That(linear.StructuralIdentity, Is.EqualTo(srgb.StructuralIdentity));
            Assert.That(linear.OutputIdentity, Is.Not.EqualTo(srgb.OutputIdentity));
            node.SetGraph(linear);
            WarmCache(renderer);
            Assert.That(RenderRed(renderer), Is.EqualTo(0.5f).Within(0.002));
            Assert.That(node.Cache.IsCached, Is.True);
            node.SetGraph(srgb);
            Assert.That(RenderRed(renderer), Is.EqualTo(0.214041f).Within(0.002));
            Assert.That(renderer.ProgramCacheStatistics.Creations, Is.EqualTo(5));
        });
    }

    [TestCase(0.5f)]
    [TestCase(2f)]
    public void PlannedSourceCropAndWarmProgramsFollowCurrentRegionValues(float scale)
    {
        OnDevice(_ =>
        {
            Rect bounds = new(4.25f, 6.5f, 64, 32);
            Rect region = new(8, 12, 10, 6);
            using var brush = (Brush.Resource)new SolidColorBrush(Colors.White).ToResource(CompositionContext.Default);
            using var rectangle = new RectangleRenderNode(bounds, brush, null);
            using var node = new GraphNode(rectangle, Chain(2, partial: true, offset: -8));
            using ProgramCache<ShaderGraphPipeline> programs = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            foreach (float offset in new[] { -8f, 8f })
            {
                node.SetGraph(Chain(2, partial: true, offset));
                using CompiledRenderRequest partial = Compile(region);
                RenderFragmentReference graph = partial.Graph.Fragments.Single(f => f.Kind == RenderFragmentKind.ShaderGraph);
                RequiredRegion required = partial.Regions.GetFragmentRequirement(graph.Inputs[0]);
                Assert.That(required.IsFull, Is.False);
                Assert.That(required.Resolve(bounds).Width, Is.LessThan(bounds.Width));
                using CompiledRenderRequest full = Compile(null);
                ushort[] fullPixels = Draw(full);
                ushort[] partialPixels = Draw(partial);
                PixelRect target = PixelRect.FromRect(bounds, scale);
                PixelRect selected = PixelRect.FromRect(region.Translate(-bounds.Position), scale);
                // Fractional target translation gives the request clip partially covered edge pixels.
                for (int y = selected.Y + 1; y < selected.Bottom - 1; y++)
                    for (int x = selected.X + 1; x < selected.Right - 1; x++)
                        for (int channel = 0; channel < 4; channel++)
                        {
                            int index = (y * target.Width + x) * 4 + channel;
                            Assert.That((float)BitConverter.UInt16BitsToHalf(partialPixels[index]),
                                Is.EqualTo((float)BitConverter.UInt16BitsToHalf(fullPixels[index])).Within(0.002));
                        }
            }
            Assert.That(programs.Statistics.Creations, Is.EqualTo(2));

            CompiledRenderRequest Compile(Rect? required) => Graphics3DTestHelpers.Compile(node,
                new(RenderIntent.Preview, RenderRequestPurpose.Frame, targetDomain: bounds, requestedRegion: required,
                    outputScale: scale, maxWorkingScale: scale, cachePolicy: RenderCacheOptions.Disabled));
            ushort[] Draw(CompiledRenderRequest request) => Graphics3DTestHelpers.ExecuteIntoPool(pool, request, bounds,
                request.Request.Options.RequestedRegion ?? bounds, scale, scale, translateToTarget: true,
                createExecutor: targets => new RenderRequestExecutor(targets, graphProgramCache: programs)).Pixels;
        });
    }

    [TestCase(0f)]
    [TestCase(0.25f)]
    [TestCase(0.75f)]
    public void SourceRegionAtTheDimensionLimitCoversTranslatedExecution(float phase)
    {
        if (GpuTestEnvironment.EnsureAvailable().MaxAttachmentDimension < BufferDimensionBudget.EngineCeiling.MaxDimension)
            Assert.Ignore("This device cannot exercise the engine's full dimension limit.");

        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            Rect bounds = new(0, 0, BufferDimensionBudget.EngineCeiling.MaxDimension, 8);
            Rect region = new(bounds.Width / 2 + phase, 2 + phase, 8, 4);
            using var brush = (Brush.Resource)new SolidColorBrush(Colors.White).ToResource(CompositionContext.Default);
            using var rectangle = new RectangleRenderNode(bounds, brush, null);
            ShaderGraphDescription description = Chain(2, partial: true);
            using var node = new GraphNode(rectangle, description);
            using var pool = new RenderTargetPool(factory: null);
            using ProgramCache<ShaderGraphPipeline> programs = ShaderGraphPipeline.CreateCache();
            using CompiledRenderRequest partial = Compile(region);
            RenderFragmentReference graph = partial.Graph.Fragments.Single(f => f.Kind == RenderFragmentKind.ShaderGraph);
            float density = description.ClampWorkingScale(bounds, graph.EffectiveScale.Value, -region.Position);
            // Fractional phases must exercise the execution-time density reduction in the review finding.
            Assert.That(density, phase == 0 ? Is.EqualTo(graph.EffectiveScale.Value) : Is.LessThan(graph.EffectiveScale.Value));
            Rect required = partial.Regions.GetFragmentRequirement(graph.Inputs[0]).Resolve(bounds);
            Assert.That(required.Width, Is.LessThan(bounds.Width / 2));
            using CompiledRenderRequest full = Compile(null);
            float[] expected = Draw(full);
            float[] actual = Draw(partial);
            Assert.That(expected, Has.Some.GreaterThan(0.9f));
            // Fractional origins can allocate an extra row/column outside the requested logical rectangle.
            int width = PixelRect.FromRect(region, 1).Width;
            for (int y = 0; y < (int)region.Height; y++)
                for (int x = 0; x < (int)region.Width; x++)
                {
                    int offset = (y * width + x) * 4;
                    Assert.That(actual.Skip(offset).Take(4), Is.EqualTo(expected.Skip(offset).Take(4)).Within(0.002));
                }

            CompiledRenderRequest Compile(Rect? requested) => Graphics3DTestHelpers.Compile(node,
                new(RenderIntent.Preview, RenderRequestPurpose.Frame, targetDomain: bounds, requestedRegion: requested,
                    outputScale: 1, maxWorkingScale: 1, cachePolicy: RenderCacheOptions.Disabled));
            float[] Draw(CompiledRenderRequest request) => Graphics3DTestHelpers.ExecuteIntoPool(pool, request, region,
                region, 1, 1, translateToTarget: true,
                createExecutor: targets => new RenderRequestExecutor(targets, graphProgramCache: programs)).Pixels
                .Select(v => (float)BitConverter.UInt16BitsToHalf(v)).ToArray();
        });
    }

    private static float RenderRed(RenderNodeRenderer renderer)
    {
        using RenderNodeRasterization raster = renderer.Rasterize();
        Assert.That(raster.Bitmap, Is.Not.Null);
        return (float)BitConverter.UInt16BitsToHalf(raster.Bitmap!.GetPixelSpan<ushort>()[0]);
    }

    private static void WarmCache(RenderNodeRenderer renderer)
    {
        for (int i = 0; i <= RenderNodeCache.StableRequestCount; i++) RenderRed(renderer);
    }

    private sealed class GraphNode(RenderNode source, ShaderGraphDescription graph) : RenderNode
    {
        private ShaderGraphDescription _graph = graph;
        private readonly RenderNode[] _children = [source];

        public override ReadOnlySpan<RenderNode> ChildNodes => _children;

        public void SetGraph(ShaderGraphDescription value)
        {
            _graph = value;
            MarkChanged();
        }

        public override void Process(RenderNodeContext context)
            => context.Publish(context.ShaderGraph(context.RecordNode(_children[0], []).Single(), _graph));
    }
}
