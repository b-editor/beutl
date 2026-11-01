using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using static Beutl.Graphics3DTests.ShaderGraphContractTests;
using static Beutl.Graphics3DTests.ShaderGraphResourceTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphImagePoolTests
{
    private static readonly Rect s_bounds = new(0, 0, 64, 48);

    [Test]
    public void ALaterExecutionTakesTheImagesAnEarlierOneReturned()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(new Bloom());
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            using var pool = new ShaderGraphImagePool();
            float[] expected = Execute(graph, s_bounds, cache: cache, sourcePixel: BrightCenter).Pixels;

            var first = Execute(graph, s_bounds, cache: cache, sourcePixel: BrightCenter, images: pool);
            long retained = pool.RetainedBytes;
            var second = Execute(graph, s_bounds, cache: cache, sourcePixel: BrightCenter, images: pool);

            Assert.Multiple(() =>
            {
                Assert.That(first.Pixels, Is.EqualTo(expected));
                Assert.That(second.Pixels, Is.EqualTo(expected));
                Assert.That(first.Statistics.ImageAllocations, Is.GreaterThan(0));
                Assert.That(retained, Is.GreaterThan(0));
                Assert.That(second.Statistics.ImageAllocations, Is.Zero);
                Assert.That(second.Statistics.ResourceReuses,
                    Is.EqualTo(first.Statistics.ResourceReuses + first.Statistics.ImageAllocations));
                Assert.That(pool.RetainedBytes, Is.EqualTo(retained), "The second execution returns the images it took.");
            });
        });
    }

    [Test]
    public void AnImageOverTheBudgetIsDisposedInsteadOfKept()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(new Bloom());
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            using var pool = new ShaderGraphImagePool(maximumRetainedBytes: 1);

            var first = Execute(graph, s_bounds, cache: cache, sourcePixel: BrightCenter, images: pool);
            var second = Execute(graph, s_bounds, cache: cache, sourcePixel: BrightCenter, images: pool);

            Assert.Multiple(() =>
            {
                Assert.That(pool.RetainedBytes, Is.Zero);
                Assert.That(second.Statistics.ImageAllocations, Is.EqualTo(first.Statistics.ImageAllocations));
                Assert.That(second.Pixels, Is.EqualTo(first.Pixels));
            });
        });
    }

    [Test]
    public void ThePoolKeepsTheLatestImagesThatFitTheBudget()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(new Bloom());
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            long all;
            using (var unlimited = new ShaderGraphImagePool())
            {
                Execute(graph, s_bounds, cache: cache, sourcePixel: BrightCenter, images: unlimited);
                all = unlimited.RetainedBytes;
            }
            using var pool = new ShaderGraphImagePool(maximumRetainedBytes: all - 1);

            Execute(graph, s_bounds, cache: cache, sourcePixel: BrightCenter, images: pool);

            Assert.That(pool.RetainedBytes, Is.InRange(1, all - 1), "The oldest images make room; the rest stay.");
        });
    }

    [Test]
    public void ImagesNoExecutionTakesAreDisposedAfterTheIdleRequests()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(new Bloom());
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            using var pool = new ShaderGraphImagePool(maximumIdleRequests: 2);

            Execute(graph, s_bounds, cache: cache, sourcePixel: BrightCenter, images: pool);
            long retained = pool.RetainedBytes;
            pool.BeginRequest();
            pool.BeginRequest();
            long afterIdleLimit = pool.RetainedBytes;
            pool.BeginRequest();

            Assert.Multiple(() =>
            {
                Assert.That(retained, Is.GreaterThan(0));
                Assert.That(afterIdleLimit, Is.EqualTo(retained), "Images idle for the limit itself stay.");
                Assert.That(pool.RetainedBytes, Is.Zero);
            });
        });
    }

    [Test]
    public void AFailedExecutionKeepsNoImage()
    {
        OnDevice(_ =>
        {
            // Both images are allocated and used before the buffer fails the budget.
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, static (builder, _) =>
            {
                ShaderImage retired = builder.Image2D("retired", builder.Source);
                ShaderImage output = builder.Image2D("output", builder.Source);
                builder.Clear("initialize", retired, ImageClearValue.Float(Vector4.One));
                builder.Copy("copy", retired, output);
                ShaderBuffer tooLarge = builder.Buffer<uint>("too-large", (int)(ShaderGraphExecutor.MaximumTransientBytes / 4 + 1));
                builder.Clear("over-budget", tooLarge);
                return builder.Output(output);
            });
            using var pool = new ShaderGraphImagePool();

            Assert.That(() => Execute(graph, new(0, 0, 4, 2), images: pool),
                Throws.InvalidOperationException.With.Message.Contains("active allocation budget"));
            Assert.That(pool.RetainedBytes, Is.Zero);
            Assert.That(Execute(ShaderGraphTests.ColorGraph(Vector4.One), new(0, 0, 4, 2), images: pool).Pixels,
                Has.All.EqualTo(1));
        });
    }

    [Test]
    public void ARenderRequestUsesThePoolItsExecutorWasGiven()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(new Bloom());
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            using var targets = new RenderTargetPool(factory: null);
            using var pool = new ShaderGraphImagePool();
            Rect bounds = ShaderGraphRenderingTests.SourceBounds;

            float[] first = ShaderGraphRenderingTests.Render(graph, bounds, cache: cache, sharedPool: targets, images: pool);
            long retained = pool.RetainedBytes;
            float[] second = ShaderGraphRenderingTests.Render(graph, bounds, cache: cache, sharedPool: targets, images: pool);

            Assert.Multiple(() =>
            {
                Assert.That(retained, Is.GreaterThan(0));
                Assert.That(pool.RetainedBytes, Is.EqualTo(retained));
                Assert.That(second, Is.EqualTo(first));
            });
        });
    }

    // The executor a custom effect creates inherits the request's settings, so its graph takes from the same pool.
    [Test]
    public void AGraphNestedInACustomEffectUsesTheRequestsPool()
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(new Bloom());
            using ProgramCache<ShaderGraphPipeline> cache = ShaderGraphPipeline.CreateCache();
            using var targets = new RenderTargetPool(factory: null);
            using var pool = new ShaderGraphImagePool();
            Rect bounds = ShaderGraphRenderingTests.SourceBounds;

            float[] first = ShaderGraphRenderingTests.Render(graph, bounds, cache: cache, sharedPool: targets,
                nestedGraph: true, images: pool);
            long retained = pool.RetainedBytes;
            float[] second = ShaderGraphRenderingTests.Render(graph, bounds, cache: cache, sharedPool: targets,
                nestedGraph: true, images: pool);

            Assert.Multiple(() =>
            {
                Assert.That(retained, Is.GreaterThan(0));
                Assert.That(pool.RetainedBytes, Is.EqualTo(retained));
                Assert.That(second, Is.EqualTo(first));
            });
        });
    }

    private static Vector4 BrightCenter(int x, int y)
        => Math.Abs(x - 32) < 4 && Math.Abs(y - 24) < 4 ? new Vector4(4, 4, 4, 1) : new Vector4(0, 0, 0, 1);
}
