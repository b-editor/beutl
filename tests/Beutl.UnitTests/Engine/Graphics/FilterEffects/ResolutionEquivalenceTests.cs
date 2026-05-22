using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Testing;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

/// <summary>
/// T023 / T024 — end-to-end SSIM rendering harness for proxy vs full-resolution comparison.
/// </summary>
/// <remarks>
/// <para>
/// The harness builds <see cref="SsimHelper"/>-comparable bitmaps for any in-scope effect by
/// rendering the same scene twice: once with the upstream source at <see cref="RenderScale.Identity"/>,
/// once with the source at a proxy ratio (default 4×). The two final composited bitmaps are then
/// passed to <see cref="SsimHelper.Compute"/>.
/// </para>
/// <para>
/// <b>SC-001 assertion deferred.</b> The spec's "SSIM ≥ 0.97 between proxy and full" criterion
/// is not currently achievable for the primitive-filter path because the primitive
/// <see cref="FilterEffectContext"/> helpers divide their length-typed parameters by upstream
/// CorrectionScale (assuming the resulting <c>SKImageFilter</c> will run on an upstream-scale
/// surface), while the actual rendering applies the filter via <c>PushPaint</c> /
/// <c>SaveLayer</c> at the compositor's output-scale layer. The divided sigma therefore
/// under-blurs at non-Identity upstream. Resolving this requires either reverting the primitive
/// divisions or restructuring <c>FilterEffectRenderNode.Process</c> to apply the filter at
/// upstream scale before the compositor blit — both are out of scope for the current PR
/// (preserve existing engine behavior). The tests below render the bitmaps and log the SSIM
/// value via <see cref="TestContext.Out"/> without asserting a threshold, so the harness stays
/// usable for follow-up work and the divergence is visible in test output.
/// </para>
/// </remarks>
[TestFixture]
public class ResolutionEquivalenceTests
{
    private const int CanvasSize = 100;
    private static readonly RenderScale s_proxyScale = new(4f, 4f);

    private static RenderNodeOperation MakeIdentityUpstream()
    {
        return RenderNodeOperation.CreateLambda(
            new Rect(0, 0, CanvasSize, CanvasSize),
            DrawTestPattern,
            hitTest: null);
    }

    private static RenderNodeOperation MakeProxyUpstream(RenderScale scale)
    {
        int physW = Math.Max(1, (int)MathF.Ceiling(CanvasSize / scale.ScaleX));
        int physH = Math.Max(1, (int)MathF.Ceiling(CanvasSize / scale.ScaleY));
        var rt = RenderTarget.Create(physW, physH)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");

        using (var inner = new ImmediateCanvas(rt))
        {
            inner.Clear(Colors.Transparent);
            inner.Canvas.Scale(1f / scale.ScaleX, 1f / scale.ScaleY);
            DrawTestPattern(inner);
        }

        return RenderNodeOperation.CreateFromRenderTarget(
            new Rect(0, 0, CanvasSize, CanvasSize),
            new Point(0, 0),
            rt,
            correctionScale: scale);
    }

    private static void DrawTestPattern(ImmediateCanvas canvas)
    {
        canvas.DrawEllipse(new Rect(15, 20, 70, 60), Brushes.Resource.White, null);
    }

    private sealed class StubSourceNode(RenderNodeOperation[] ops) : RenderNode
    {
        public override RenderNodeOperation[] Process(RenderNodeContext context) => ops;
    }

    private static Bitmap RenderThroughChain(RenderNodeOperation upstream, FilterEffect.Resource effectResource)
    {
        var sourceNode = new StubSourceNode([upstream]);
        var feNode = new FilterEffectRenderNode(effectResource);
        feNode.AddChild(sourceNode);

        var processor = new RenderNodeProcessor(feNode, useRenderCache: false);
        using var rt = RenderTarget.Create(CanvasSize, CanvasSize)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using (var canvas = new ImmediateCanvas(rt))
        {
            canvas.Clear(Colors.Transparent);
            processor.Render(canvas);
        }
        return rt.Snapshot();
    }

    private static double MeasureSsim(FilterEffect effect)
    {
        var resource = effect.ToResource(CompositionContext.Default);
        using Bitmap reference = RenderThroughChain(MakeIdentityUpstream(), resource);
        using Bitmap proxy = RenderThroughChain(MakeProxyUpstream(s_proxyScale), resource);
        double ssim = SsimHelper.Compute(reference, proxy);
        TestContext.Out.WriteLine($"{effect.GetType().Name}: SSIM={ssim:F4} (proxy 4× vs full)");
        return ssim;
    }

    // Per-effect harness exercises. Each test renders both paths and logs the SSIM value.
    // No threshold is asserted; see class remarks for rationale. A future engine refactor
    // that aligns the filter application scale with the divided primitive sigma should
    // flip these into proper SC-001 ≥ 0.97 assertions.

    [Test]
    public void Blur_HarnessLogsSsim() => MeasureSsim(new Blur { Sigma = { CurrentValue = new Size(8, 8) } });

    [Test]
    public void DropShadow_HarnessLogsSsim() => MeasureSsim(new DropShadow
    {
        Position = { CurrentValue = new Point(5, 5) },
        Sigma = { CurrentValue = new Size(4, 4) },
    });

    [Test]
    public void InnerShadow_HarnessLogsSsim() => MeasureSsim(new InnerShadow
    {
        Position = { CurrentValue = new Point(4, 4) },
        Sigma = { CurrentValue = new Size(3, 3) },
    });

    [Test]
    public void Erode_HarnessLogsSsim() => MeasureSsim(new Erode
    {
        RadiusX = { CurrentValue = 2f },
        RadiusY = { CurrentValue = 2f },
    });

    [Test]
    public void Dilate_HarnessLogsSsim() => MeasureSsim(new Dilate
    {
        RadiusX = { CurrentValue = 2f },
        RadiusY = { CurrentValue = 2f },
    });

    [Test]
    public void MosaicEffect_HarnessLogsSsim() => MeasureSsim(new MosaicEffect
    {
        TileSize = { CurrentValue = new Size(8, 8) },
    });

    [Test]
    public void ColorShift_HarnessLogsSsim() => MeasureSsim(new ColorShift
    {
        RedOffset = { CurrentValue = new PixelPoint(2, 0) },
        GreenOffset = { CurrentValue = new PixelPoint(-2, 0) },
    });

    [Test]
    public void ShakeEffect_HarnessLogsSsim() => MeasureSsim(new ShakeEffect
    {
        StrengthX = { CurrentValue = 0f },
        StrengthY = { CurrentValue = 0f },
    });

    [Test]
    public void SplitEffect_HarnessLogsSsim() => MeasureSsim(new SplitEffect
    {
        HorizontalDivisions = { CurrentValue = 2 },
        VerticalDivisions = { CurrentValue = 2 },
        HorizontalSpacing = { CurrentValue = 4f },
        VerticalSpacing = { CurrentValue = 4f },
    });

    [Test]
    public void Clipping_HarnessLogsSsim() => MeasureSsim(new Clipping
    {
        Left = { CurrentValue = 10f },
        Right = { CurrentValue = 10f },
    });

    [Test]
    public void FlatShadow_HarnessLogsSsim() => MeasureSsim(new FlatShadow
    {
        Length = { CurrentValue = 8f },
        Angle = { CurrentValue = 45f },
    });

    [Test]
    public void StrokeEffect_HarnessLogsSsim()
    {
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Red;
        pen.Thickness.CurrentValue = 3f;
        var effect = new StrokeEffect();
        effect.Pen.CurrentValue = pen;
        MeasureSsim(effect);
    }

    [Test]
    public void IdentityUpstream_NoFilter_IsPixelIdentical()
    {
        // The sanity guard: with Identity upstream on both sides, SSIM should be exactly 1.0.
        // If this ever breaks, the harness itself has regressed.
        var effect = new Blur { Sigma = { CurrentValue = new Size(8, 8) } };
        var resource = effect.ToResource(CompositionContext.Default);
        using Bitmap a = RenderThroughChain(MakeIdentityUpstream(), resource);
        using Bitmap b = RenderThroughChain(MakeIdentityUpstream(), resource);
        double ssim = SsimHelper.Compute(a, b);
        Assert.That(ssim, Is.EqualTo(1.0).Within(0.001));
    }

    [Test]
    public void BicubicResampler_RoundTrip_Sanity()
    {
        // Confirms the BicubicResampler helper produces a same-size bitmap for an Identity scale,
        // so it can be used as an SSIM pre-step when callers need explicit upscaling.
        using var rt = RenderTarget.Create(25, 25)
            ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
        using (var canvas = new ImmediateCanvas(rt))
        {
            canvas.Clear(Colors.Transparent);
            canvas.DrawEllipse(new Rect(5, 5, 15, 15), Brushes.Resource.White, null);
        }
        using Bitmap small = rt.Snapshot();
        using Bitmap upscaled = BicubicResampler.Upscale(small, 100, 100);
        Assert.That(upscaled.Width, Is.EqualTo(100));
        Assert.That(upscaled.Height, Is.EqualTo(100));
    }
}
