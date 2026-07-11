using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

/// <summary>
/// Pins the backward-ROI / full-frame-anchor family (contract A3), the siblings of
/// <see cref="PositionAnchoredCropTests"/>. A downstream deflating pass (a fixed <see cref="Clipping"/>) ROI-crops an
/// intermediate pass to an OFFSET sub-rect, and the internal backward walk (execution-plan C3.1) propagates that crop
/// up every pass's <see cref="CompiledPass.BackwardBounds"/> map.
/// <list type="bullet">
/// <item><see cref="SKSLScriptEffect"/>'s generator branch derives fragCoord/width/height from the pass output rect,
/// so an <c>Identity</c> contract lets a crop re-anchor the pattern into the sub-rect (must be <c>RenderTime</c>) —
/// probed at the pixel level.</item>
/// <item><see cref="ShakeEffect"/> translates its input, so its backward must claim <c>r − translate</c>; an identity
/// backward under-claims and crops an upstream pass, losing the translated source band.</item>
/// <item><see cref="StrokeEffect"/> contour-traces the whole materialized input, so its backward must claim the full
/// input; an identity backward feeds a cropped snapshot into the tracer and drops content outside the ROI.</item>
/// <item><see cref="TransformEffect"/>'s ApplyToTarget pass rotates/scales its input, so its backward must inverse-map
/// the requested region; an identity backward crops the upstream to the un-transformed region and loses pulled-in
/// pixels. RenderTime is unavailable (the forward inflates), so the pass keeps the inflating forward and inverts.</item>
/// </list>
/// The three backward-map defects are asserted directly against the compiled backward map — the faithful probe used by
/// <see cref="PositionAnchoredCropTests.FlatShadow_BackwardRoi_CoversExtrusionSourceBand"/> — because a blur/clip
/// pixel chain confounds the under-claim with Skia's edge-clamp fill of the cropped band.
/// </summary>
[NonParallelizable]
[TestFixture]
public class BackwardRoiContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
        VulkanTestEnvironment.EnsureAvailable();
    }

    // A generator (no `src` child) whose colour is a function of the full-frame device coordinate. Under Identity the
    // downstream clip bakes it into an OFFSET sub-rect with a LOCAL fragCoord origin and a sub-rect width/height, so
    // the ramp rescales and shifts; RenderTime keeps it baking full-frame.
    [Test]
    public void SkslGenerator_KeptRegionUnshiftedUnderDeflatingClip()
    {
        var effect = new SKSLScriptEffect();
        effect.Script.CurrentValue =
            """
            uniform float width;
            uniform float height;
            half4 main(float2 fragCoord) {
                float u = fragCoord.x / width;
                float v = fragCoord.y / height;
                return half4(u, v, 0.25, 1.0);
            }
            """;

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap uncropped = RenderChain([effect], GradientInput, Rect.Invalid);
            using Bitmap cropped = RenderChain([effect, MakeClip()], GradientInput, Rect.Invalid);

            double meanDiff = MeanChannelDiff(uncropped, cropped, 40, 45, 150, 115);
            TestContext.WriteLine($"kept-region mean channel diff = {meanDiff:F3} (tolerance 3)");
            Assert.That(meanDiff, Is.LessThanOrEqualTo(3.0),
                "the clipped kept region must match the uncropped generator render (no crop-offset rescale)");
        });
    }

    // Shake translates its input by `translate`, so producing output region R samples the input over R − translate;
    // the backward map must translate by the inverse. Identity (r => r) under-claims and crops the upstream.
    [Test]
    public void Shake_BackwardRoi_ClaimsTranslatedSourceBand()
    {
        var shake = new ShakeEffect
        {
            Id = new Guid("00000000-0000-0000-0000-000000000004"),
            StrengthX = { CurrentValue = 60 },
            StrengthY = { CurrentValue = 60 },
            Speed = { CurrentValue = 2 },
        };

        CompiledPlan plan = Compile(shake);
        var requested = new Rect(50, 40, 40, 30);

        // The shake vector is Id-derived and internal; recover it from the forward map (forward = r.Translate(+t)).
        Rect forward = plan.Passes[0].ForwardBounds(requested);
        var translate = new Vector(forward.X - requested.X, forward.Y - requested.Y);
        Assert.That(translate, Is.Not.EqualTo(default(Vector)), "the pinned Id must yield a non-zero shake vector");

        Rect required = plan.Passes[0].BackwardBounds(requested);
        Assert.That(required, Is.EqualTo(requested.Translate(-translate)),
            $"Shake backward({requested}) = {required} must claim the translated source band {requested.Translate(-translate)}");
    }

    // Stroke contour-traces the WHOLE materialized input, so its backward must claim the full input regardless of the
    // requested output region. Identity (r => r) under-claims and feeds a cropped snapshot into the tracer.
    [Test]
    public void Stroke_BackwardRoi_ClaimsFullInput()
    {
        var pen = new Pen();
        pen.Thickness.CurrentValue = 14;
        pen.Brush.CurrentValue = Brushes.Red;
        var stroke = new StrokeEffect { Pen = { CurrentValue = pen } };

        CompiledPlan plan = Compile(stroke);
        var requested = new Rect(60, 50, 20, 20);

        Rect required = plan.Passes[0].BackwardBounds(requested);
        Assert.That(required, Is.EqualTo(s_bounds),
            $"Stroke backward({requested}) = {required} must claim the full input {s_bounds} (global contour tracing)");
    }

    // TransformEffect.ApplyToTarget rotates its input around the pass centre, so its backward must inverse-map the
    // requested region (pivoted on the same input bounds the forward uses). Identity (r => r) under-claims.
    [Test]
    public void Transform_BackwardRoi_InverseMapsRequestedRegion()
    {
        var rot = new RotationTransform();
        rot.Rotation.CurrentValue = 30f;
        var effect = new TransformEffect();
        effect.Transform.CurrentValue = rot;

        CompiledPlan plan = Compile(effect);
        var requested = new Rect(130, 40, 25, 15);

        // Reconstruct the forward transform (pivot on the full input bounds) and invert it, matching production.
        Matrix mat = rot.CreateMatrix(CompositionContext.Default);
        Vector origin = RelativePoint.Center.ToPixels(s_bounds.Size) + s_bounds.Position;
        Matrix offset = Matrix.CreateTranslation(origin);
        Matrix transform = (-offset) * mat * offset;
        Assert.That(transform.TryInvert(out Matrix inverted), Is.True);
        Rect expected = requested.TransformToAABB(inverted);

        Rect required = plan.Passes[0].BackwardBounds(requested);
        Assert.That(required, Is.Not.EqualTo(requested), "an identity backward under-claims a rotated pass");
        AssertRectClose(required, expected, tolerance: 0.5);
    }

    // PathFollow translates (FollowRotation off) its input along the path, so producing output region R samples the
    // input over R − translate; the backward must inverse-map. Identity (r => r) under-claims and crops the upstream.
    [Test]
    public void PathFollow_BackwardRoi_InverseMapsFollowTranslation()
    {
        var figure = new PathFigure();
        figure.StartPoint.CurrentValue = new Point(0, 0);
        figure.Segments.Add(new LineSegment(new Point(100, 0)));
        var geometry = new PathGeometry { Figures = { figure } };

        var effect = new PathFollowEffect { Progress = { CurrentValue = 50f } };
        effect.Geometry.CurrentValue = geometry;

        CompiledPlan plan = Compile(effect);
        var requested = new Rect(50, 40, 40, 30);

        // Recover the (non-rotating) follow translation from the forward map (forward = r + t).
        Rect forward = plan.Passes[0].ForwardBounds(requested);
        var translate = new Vector(forward.X - requested.X, forward.Y - requested.Y);
        Assert.That(translate, Is.Not.EqualTo(default(Vector)), "the path must yield a non-zero follow translation");

        Rect required = plan.Passes[0].BackwardBounds(requested);
        AssertRectClose(required, requested.Translate(-translate), tolerance: 0.5);
    }

    // InnerShadow draws a blurred (3σ), offset (Position) shadow copy, so its backward must claim r ∪ (r − Position)
    // inflated by 3σ (the DropShadow pattern). Identity (r => r) under-claims and crops the offset shadow source.
    [Test]
    public void InnerShadow_BackwardRoi_CoversOffsetShadowSource()
    {
        var effect = new InnerShadow { Position = { CurrentValue = new Point(30, 0) } };

        CompiledPlan plan = Compile(effect);
        var requested = new Rect(50, 40, 40, 30);
        Rect expected = requested.Union(requested.Translate(new Vector(-30, 0)));

        Rect required = plan.Passes[0].BackwardBounds(requested);
        Assert.That(required, Is.EqualTo(expected),
            $"InnerShadow backward({requested}) = {required} must cover the offset shadow source {expected}");
    }

    // Erode is a min filter: an output pixel reads its radius neighborhood, so its backward must inflate by the radius
    // (mirroring Dilate) even though erode never grows the forward bounds. Identity (r => r) under-claims and, under a
    // downstream deflate, starves the crop edge of the neighbor texels it reads (over-eroding it).
    [Test]
    public void Erode_BackwardRoi_InflatesByRadius()
    {
        var effect = new Erode { RadiusX = { CurrentValue = 10 }, RadiusY = { CurrentValue = 6 } };

        CompiledPlan plan = Compile(effect);
        var requested = new Rect(50, 40, 40, 30);
        Rect expected = requested.Inflate(new Thickness(10, 6));

        Assert.Multiple(() =>
        {
            Assert.That(plan.Passes[0].ForwardBounds(requested), Is.EqualTo(requested),
                "erode never grows the forward bounds (forward is identity)");
            Assert.That(plan.Passes[0].BackwardBounds(requested), Is.EqualTo(expected),
                $"Erode backward({requested}) must inflate by the radius {expected} (min-filter neighborhood), not identity");
        });
    }

    // The effect radii are unconstrained floats; a negative value must clamp to zero at the builder seam or the
    // bounds maps DEFLATE (backward under-claims and crops upstream content).
    [Test]
    public void ErodeAndDilate_NegativeRadii_ClampToIdentityBounds()
    {
        var erode = new Erode { RadiusX = { CurrentValue = -10 }, RadiusY = { CurrentValue = -6 } };
        var dilate = new Dilate { RadiusX = { CurrentValue = -10 }, RadiusY = { CurrentValue = -6 } };

        CompiledPlan erodePlan = Compile(erode);
        CompiledPlan dilatePlan = Compile(dilate);
        var requested = new Rect(50, 40, 40, 30);

        Assert.Multiple(() =>
        {
            Assert.That(erodePlan.Passes[0].BackwardBounds(requested), Is.EqualTo(requested),
                "a negative erode radius must clamp to zero, never deflate the backward ROI");
            Assert.That(dilatePlan.Passes[0].ForwardBounds(requested), Is.EqualTo(requested),
                "a negative dilate radius must clamp to zero, never deflate the forward bounds");
            Assert.That(dilatePlan.Passes[0].BackwardBounds(requested), Is.EqualTo(requested),
                "a negative dilate radius must clamp to zero, never deflate the backward ROI");
        });
    }

    private static CompiledPlan Compile(FilterEffect effect)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));
        using EffectGraph graph = builder.Build();
        return EffectGraphCompiler.Compile(graph, diagnostics: null);
    }

    private static void AssertRectClose(Rect actual, Rect expected, double tolerance)
    {
        Assert.Multiple(() =>
        {
            Assert.That(Math.Abs(actual.X - expected.X), Is.LessThanOrEqualTo(tolerance), $"X: {actual} vs {expected}");
            Assert.That(Math.Abs(actual.Y - expected.Y), Is.LessThanOrEqualTo(tolerance), $"Y: {actual} vs {expected}");
            Assert.That(Math.Abs(actual.Width - expected.Width), Is.LessThanOrEqualTo(tolerance), $"W: {actual} vs {expected}");
            Assert.That(Math.Abs(actual.Height - expected.Height), Is.LessThanOrEqualTo(tolerance), $"H: {actual} vs {expected}");
        });
    }

    private static Clipping MakeClip() => new()
    {
        Left = { CurrentValue = 20 },
        Top = { CurrentValue = 30 },
    };

    private static Bitmap RenderChain(FilterEffect[] effects, Func<RenderNodeOperation> input, Rect requestedBounds)
    {
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        foreach (FilterEffect effect in effects)
            effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));

        using EffectGraph graph = builder.Build();
        using var pool = new RenderTargetPool();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        FrameResources frame = EffectGraphCompiler.ResolveResources(plan, requestedBounds, workingScale: 1f);
        RenderNodeOperation[] ops = PlanExecutor.Execute(
            plan, frame, [input()], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool);

        int w = (int)s_bounds.Width, h = (int)s_bounds.Height;
        using RenderTarget target = RenderTarget.Create(w, h)!;
        using (var canvas = new ImmediateCanvas(target, 1f, logicalSize: s_bounds.Size))
        {
            canvas.Clear(Colors.Black);
            foreach (RenderNodeOperation op in ops)
                op.Render(canvas);
        }

        RenderNodeOperation.DisposeAll(ops);
        return target.Snapshot();
    }

    // A smooth cross-gradient input covering the whole frame; a crop-offset rescale of the generator is unmistakable.
    private static RenderNodeOperation GradientInput()
    {
        var brush = new LinearGradientBrush();
        brush.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        brush.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
        brush.GradientStops.Add(new GradientStop(Colors.White, 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 30, 60, 120), 1));
        Brush.Resource brushResource = (Brush.Resource)brush.ToResource(CompositionContext.Default);
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, brushResource, null),
            hitTest: s_bounds.Contains);
    }

    private static double MeanChannelDiff(Bitmap a, Bitmap b, int x0, int y0, int x1, int y1)
    {
        long sum = 0;
        long count = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                SkiaSharp.SKColor ca = a.SKBitmap.GetPixel(x, y);
                SkiaSharp.SKColor cb = b.SKBitmap.GetPixel(x, y);
                sum += Math.Abs(ca.Red - cb.Red);
                sum += Math.Abs(ca.Green - cb.Green);
                sum += Math.Abs(ca.Blue - cb.Blue);
                count += 3;
            }
        }

        return count == 0 ? 0 : (double)sum / count;
    }
}
