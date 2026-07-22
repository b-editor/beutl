using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
[TestFixture]
public class NodeCacheScaleTests
{
    private static readonly Rect s_bounds = new(0, 0, 100, 100);

    private static EllipseRenderNode CacheableEllipse()
    {
        var node = new EllipseRenderNode(s_bounds, Brushes.Resource.White, null);
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        return node;
    }

    [TestCase(0.5f)]
    [TestCase(1.0f)]
    public void FrameCache_RecordsResolvedDensity_WhileMetadataRemainsCacheIndependent(float outputScale)
    {
        using EllipseRenderNode node = CacheableEllipse();
        using var renderer = CreateFrameRenderer(
            node,
            outputScale,
            maxWorkingScale: 2f * outputScale);

        using (renderer.Rasterize())
        {
        }
        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(node.Cache.Density, Is.EqualTo(outputScale));
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.EffectiveScale.IsUnbounded, Is.True,
                "metadata must retain the original graph instead of substituting a pixel cache");
        });
    }

    [Test]
    public void HighDensitySource_IsCachedAtItsResolvedSupplyDensity()
    {
        using var node = new ConcreteSourceNode();
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        using var renderer = CreateFrameRenderer(node, outputScale: 1f, maxWorkingScale: 8f);

        using (renderer.Rasterize())
        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.Cache.IsCached, Is.True);
            Assert.That(node.Cache.Density, Is.EqualTo(4f));
            Assert.That(node.ExecuteCount, Is.EqualTo(1), "the warm frame must use the cached producer output");
            Assert.That(renderer.Measure().EffectiveScale.Value, Is.EqualTo(4f));
        });
    }

    [Test]
    public void FrameCache_TargetSizeMatchesResolvedDensity()
    {
        using EllipseRenderNode node = CacheableEllipse();
        using var renderer = CreateFrameRenderer(node, outputScale: 0.5f, maxWorkingScale: 1f);

        using (renderer.Rasterize())
        {
        }

        Assert.That(node.Cache.IsCached, Is.True);
        foreach ((RenderTarget target, Rect bounds) in node.Cache.UseCache())
        {
            using (target)
            {
                PixelRect expectedDeviceBounds = RenderScaleUtilities.AddRasterApron(
                    PixelRect.FromRect(bounds, 0.5f));
                Assert.That(target.Width, Is.EqualTo(expectedDeviceBounds.Width));
                Assert.That(target.Height, Is.EqualTo(expectedDeviceBounds.Height));
            }
        }
    }

    [Test]
    public void CacheRuleBypass_IsRequestPolicyAndDoesNotPoisonLaterEligibleFrames()
    {
        using EllipseRenderNode node = CacheableEllipse();
        using (RenderNodeRenderer excluded = CreateFrameRenderer(
                   node,
                   outputScale: 1f,
                   maxWorkingScale: 1f,
                   cacheRules: new RenderCacheRules(9_999, 1)))
        using (excluded.Rasterize())
        {
        }

        Assert.That(node.Cache.IsCached, Is.False);

        using (RenderNodeRenderer eligible = CreateFrameRenderer(
                   node,
                   outputScale: 1f,
                   maxWorkingScale: 1f,
                   cacheRules: RenderCacheRules.Default))
        using (eligible.Rasterize())
        {
        }

        Assert.That(node.Cache.IsCached, Is.True);
    }

    private static RenderNodeRenderer CreateFrameRenderer(
        RenderNode node,
        float outputScale,
        float maxWorkingScale,
        RenderCacheRules? cacheRules = null)
        => new(
            node,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_bounds,
                OutputScale = outputScale,
                MaxWorkingScale = maxWorkingScale,
                UseRenderCache = true,
                CacheRules = cacheRules ?? RenderCacheRules.Default,
                RenderPurpose = RenderRequestPurpose.Frame,
                TargetFactory = new CpuTargetFactory(),
            });

    private sealed class ConcreteSourceNode : RenderNode
    {
        public int ExecuteCount { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            Brush.Resource fill = Brushes.Resource.White;
            RenderResource<Brush.Resource> fillResource = context.Borrow(
                fill,
                fill.GetOriginal().Id,
                fill.Version);
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                execute: session =>
                {
                    ExecuteCount++;
                    session.UseResource(fillResource, currentFill =>
                    {
                        using OpaqueRenderOutput output = session.CreateOutput(s_bounds);
                        output.Canvas.Use(canvas => canvas.DrawRectangle(s_bounds, currentFill, null));
                        session.Publish(output);
                    });
                },
                bounds: RenderOperationBoundsContract.Source(s_bounds),
                hitTest: RenderHitTestContract.None,
                valueCardinality: RenderValueCardinality.Single,
                scale: RenderScaleContract.Custom(
                    static _ => 4f,
                    structuralKey: typeof(ConcreteSourceNode)),
                structuralKey: typeof(ConcreteSourceNode),
                runtimeIdentity: new RenderRuntimeIdentity(4f),
                resources: [fillResource]);
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public RenderTarget Create(PixelSize deviceSize)
            => new CpuRenderTarget(deviceSize);

        private sealed class CpuRenderTarget : RenderTarget
        {
            public CpuRenderTarget(PixelSize size)
                : base(
                    SKSurface.Create(new SKImageInfo(
                        size.Width,
                        size.Height,
                        SKColorType.RgbaF16,
                        SKAlphaType.Premul,
                        s_colorSpace))
                    ?? throw new InvalidOperationException("Could not create a CPU cache-scale test target."),
                    size.Width,
                    size.Height)
            {
            }
        }
    }
}
