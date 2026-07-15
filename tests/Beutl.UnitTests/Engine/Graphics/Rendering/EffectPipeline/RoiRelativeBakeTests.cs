using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the absolute-anchor bugs (review round 3, M2/M3): an identity-bounds whole-source pass that bakes
/// full-buffer-absolute uniforms (Mosaic's tile grid, DisplacementMap's pivot) is corrupted when a downstream
/// bounds-DEFLATING pass (a fixed <see cref="Clipping"/>) crops its resolved ROI to a sub-rect. The backward-ROI
/// walk is active in production for INTERMEDIATE passes (M5): <c>ResolveLastRoi</c> seeds the last pass from its own
/// <c>OutputBounds</c> when the request is <c>Rect.Invalid</c>, then propagates concrete (deflating) ROIs upstream.
/// The fix declares these passes <see cref="BoundsContract.FullFrame"/> so they always bake at full input bounds,
/// keeping the absolute uniforms consistent with the baked buffer.
/// </summary>
[NonParallelizable]
[TestFixture]
public class RoiRelativeBakeTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
    }

    // A fixed Clipping downstream deflates its own output bounds; the ROI walk would crop the upstream Mosaic pass to
    // that deflated sub-rect. Mosaic's tile grid is authored in the FULL-frame device space, so it must bake at full
    // bounds regardless of the downstream crop. Assert the Mosaic pass resolves to the full frame, not the sub-rect.
    [Test]
    public void MosaicUnderDeflatingClip_ResolvesFullFrame_NotDeflatedRoi()
    {
        FrameResources frame = ResolveChain(
            new MosaicEffect { TileSize = { CurrentValue = new Size(16, 16) } },
            MakeClip());

        Assert.That(frame.Passes[0].OutputRoi, Is.EqualTo(s_bounds),
            "the Mosaic pass bakes at full input bounds even under a downstream deflating clip");
    }

    // Same absolute-anchor family for the displacement scale/rotation pivot (M3): the pivot is (Bounds/2 + center)*w
    // in the full-frame device space, so the pass must bake full-frame under a deflating downstream.
    [Test]
    public void DisplacementScaleUnderDeflatingClip_ResolvesFullFrame_NotDeflatedRoi()
    {
        FrameResources frame = ResolveDisplacementChain(new DisplacementMapScaleTransform
        {
            Scale = { CurrentValue = 150 },
        });

        Assert.That(frame.Passes[0].OutputRoi, Is.EqualTo(s_bounds),
            "the displacement-scale pass bakes at full input bounds even under a downstream deflating clip");
    }

    [Test]
    public void DisplacementRotationUnderDeflatingClip_ResolvesFullFrame_NotDeflatedRoi()
    {
        FrameResources frame = ResolveDisplacementChain(new DisplacementMapRotationTransform
        {
            Rotation = { CurrentValue = 30 },
        });

        Assert.That(frame.Passes[0].OutputRoi, Is.EqualTo(s_bounds),
            "the displacement-rotation pass bakes at full input bounds even under a downstream deflating clip");
    }

    // The visible symptom (M2): mosaicking an opaque source then clipping must leave the clipped region fully
    // covered. The absolute-frame bug samples tile centres beyond the deflated buffer (Decal -> transparent), so the
    // clipped region loses edge tiles. Reference coverage is the same clip over the same source WITHOUT the mosaic.
    [Test]
    public void MosaicThenClip_ClippedRegionStaysFullyCovered()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            int mosaicCovered = RenderAndCountOpaque(
                [new MosaicEffect { TileSize = { CurrentValue = new Size(16, 16) } }, MakeClip()]);
            int clipOnlyCovered = RenderAndCountOpaque([MakeClip()]);

            TestContext.WriteLine($"mosaic+clip opaque px = {mosaicCovered}, clip-only opaque px = {clipOnlyCovered}");
            Assert.That(mosaicCovered, Is.GreaterThanOrEqualTo((int)(clipOnlyCovered * 0.98)),
                "the mosaicked clip region stays as covered as the un-mosaicked clip (no transparent edge tiles)");
        });
    }

    private static Clipping MakeClip() => new()
    {
        Left = { CurrentValue = 24 },
        Top = { CurrentValue = 24 },
        Right = { CurrentValue = 24 },
        Bottom = { CurrentValue = 24 },
    };

    private static FrameResources ResolveChain(params FilterEffect[] effects)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        foreach (FilterEffect effect in effects)
            effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        return EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
    }

    private static FrameResources ResolveDisplacementChain(DisplacementMapTransform transform)
    {
        var map = new LinearGradientBrush();
        map.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        map.EndPoint.CurrentValue = new RelativePoint(1, 0, RelativeUnit.Relative);
        map.GradientStops.Add(new GradientStop(Colors.Black, 0));
        map.GradientStops.Add(new GradientStop(Colors.White, 1));

        var effect = new DisplacementMapEffect
        {
            DisplacementMap = { CurrentValue = map },
            Transform = { CurrentValue = transform },
            Channel = { CurrentValue = DisplacementMapChannel.Luminance },
        };
        return ResolveChain(effect, MakeClip());
    }

    private static int RenderAndCountOpaque(FilterEffect[] effects)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        foreach (FilterEffect effect in effects)
            effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [OpaqueInput()], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery);

        int w = (int)s_bounds.Width, h = (int)s_bounds.Height;
        using RenderTarget target = RenderTarget.Create(w, h)!;
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1f, logicalSize: s_bounds.Size))
        {
            canvas.Clear(Colors.Black);
            foreach (RenderNodeOperation op in ops)
                op.Render(canvas);
        }

        RenderNodeOperation.DisposeAll(ops);
        using Bitmap bmp = target.Snapshot();
        int opaque = 0;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (bmp.SKBitmap.GetPixel(x, y).Red > 40)
                    opaque++;
            }
        }

        return opaque;
    }

    private static RenderNodeOperation OpaqueInput()
        => RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);
}
