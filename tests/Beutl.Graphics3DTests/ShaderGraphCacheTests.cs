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
