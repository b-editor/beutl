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
/// Pins the position-anchored geometry-pass family (A3): a geometry pass whose callback anchors its content to the
/// pass output rect — <see cref="BlendEffect"/>'s non-solid brush, <see cref="DisplacementMapEffect"/>'s
/// show-map branch, <see cref="FlatShadow"/> — must render the kept region identically whether or not a downstream
/// deflating pass (a fixed <see cref="Clipping"/>) crops its resolved output to an OFFSET sub-rect. Before the fix
/// the executor bakes the pass into a sub-rect whose origin is the crop position while the callback still anchors to
/// <c>new Rect(session.Bounds.Size)</c> / draws the input at <c>default</c>, shifting the content by the crop offset.
/// FlatShadow additionally under-claims its backward ROI (<c>r =&gt; r</c>), which can crop an UPSTREAM pass below the
/// extrusion source band.
/// </summary>
[NonParallelizable]
[TestFixture]
public class PositionAnchoredCropTests
{
    private static readonly Rect s_bounds = new(0, 0, 160, 120);
    private const float ClipLeft = 20;
    private const float ClipTop = 30;

    // An interior window well inside the kept region [~19,160) x [~29,120), avoiding the 1px clip-boundary quirk.
    private const int WinX0 = 40, WinX1 = 150, WinY0 = 45, WinY1 = 115;

    [OneTimeSetUp]
    public void SeedLoggerFactory()
    {
        Log.LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(static _ => { });
        VulkanTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void BlendEffect_NonSolidBrush_KeptRegionUnshiftedUnderDeflatingClip()
    {
        var brush = new LinearGradientBrush();
        brush.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        brush.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(200, 255, 0, 0), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 255), 1));

        var effect = new BlendEffect
        {
            Brush = { CurrentValue = brush },
            BlendMode = { CurrentValue = BlendMode.SrcOver },
        };

        AssertKeptRegionMatchesUncropped(effect, tolerance: 3.0, GradientInput);
    }

    // AutoClip tightens its operation to an offset content rect at render time, while the following RenderTime brush
    // blend keeps the full resolved session bounds. The input blit must bridge those origins instead of moving the
    // tightened pixels to the session's top-left corner.
    [Test]
    public void BlendEffect_NonSolidBrush_PreservesUpstreamAutoClipOffset()
    {
        var clip = new Clipping { AutoClip = { CurrentValue = true } };
        var brush = new LinearGradientBrush();
        brush.GradientStops.Add(new GradientStop(Colors.Red, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Blue, 1));
        var blend = new BlendEffect
        {
            Brush = { CurrentValue = brush },
            BlendMode = { CurrentValue = BlendMode.SrcIn },
        };

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap bitmap = RenderChain(
                [clip, blend], OffsetShapeInput, Rect.Invalid, clearTransparent: true);
            Rect alphaBounds = FindAlphaBounds(bitmap);

            Assert.Multiple(() =>
            {
                Assert.That(alphaBounds.X, Is.GreaterThan(60),
                    "the tightened input must remain at its original horizontal offset, not move to x=0");
                Assert.That(alphaBounds.Y, Is.GreaterThan(35),
                    "the tightened input must remain at its original vertical offset, not move to y=0");
            });
        });
    }

    [Test]
    public void DisplacementMapShow_KeptRegionUnshiftedUnderDeflatingClip()
    {
        var map = new LinearGradientBrush();
        map.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
        map.EndPoint.CurrentValue = new RelativePoint(1, 0, RelativeUnit.Relative);
        map.GradientStops.Add(new GradientStop(Colors.Black, 0));
        map.GradientStops.Add(new GradientStop(Colors.White, 1));

        var effect = new DisplacementMapEffect
        {
            DisplacementMap = { CurrentValue = map },
            ShowDisplacementMap = { CurrentValue = true },
        };

        AssertKeptRegionMatchesUncropped(effect, tolerance: 3.0, GradientInput);
    }

    // FlatShadow uses a solid brush by default, so a downstream Clipping's own origin bridge fully masks the shift
    // (the chain renders correctly). The faithful probe of FlatShadow's OWN registration is a bounds-honest reader
    // under an OFFSET render-request ROI: the pass must bake its content registered to its resolved sub-rect, not to
    // its un-cropped OutputBounds origin. A crop-offset shift makes the directly-composited op mismatch the full render.
    [Test]
    public void FlatShadow_KeptRegionUnshiftedUnderOffsetRoi()
    {
        FlatShadow Make() => new()
        {
            Angle = { CurrentValue = 30 },
            Length = { CurrentValue = 24 },
            ShadowOnly = { CurrentValue = false },
        };

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = RenderChain([Make()], ShapeInput, Rect.Invalid);
            using Bitmap cropped = RenderChain([Make()], ShapeInput, new Rect(19, 29, 140, 90));

            double meanDiff = MeanChannelDiff(full, cropped, WinX0, WinY0, WinX1, WinY1);
            TestContext.WriteLine($"kept-region mean channel diff = {meanDiff:F3} (tolerance 8)");
            Assert.That(meanDiff, Is.LessThanOrEqualTo(8.0),
                "the offset-ROI FlatShadow output must register to its sub-rect (no crop-offset shift)");
        });
    }

    // FlatShadow's brush fill was anchored to session.Bounds (the sub-rect under an offset ROI) and drawn without the
    // origin bridge the shadow/path draws already use. A NON-solid brush therefore re-anchors into the sub-rect and
    // shifts, so the kept region mismatches the un-cropped render even though the silhouette registers. A hard-stop
    // gradient (red|blue at 0.5) makes the re-anchor move a full-contrast colour edge, and the window sits inside the
    // shadow interior where the SrcIn fill is visible. With the fix the brush anchors to the un-cropped output frame
    // and fills inside the same bridge scope.
    [Test]
    public void FlatShadow_GradientBrush_KeptRegionUnshiftedUnderOffsetRoi()
    {
        FlatShadow Make()
        {
            var brush = new LinearGradientBrush();
            brush.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Relative);
            brush.EndPoint.CurrentValue = new RelativePoint(1, 1, RelativeUnit.Relative);
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 0, 0), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 0, 0), 0.5f));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 255), 0.5f));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 0, 0, 255), 1));
            return new FlatShadow
            {
                Angle = { CurrentValue = 30 },
                Length = { CurrentValue = 24 },
                Brush = { CurrentValue = brush },
                ShadowOnly = { CurrentValue = true },
            };
        }

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = RenderChain([Make()], ShapeInput, Rect.Invalid);
            using Bitmap cropped = RenderChain([Make()], ShapeInput, new Rect(19, 29, 140, 90));

            // Tolerance 3 (not the file's usual 8): a correctly anchored brush renders the interior window
            // identically (~0 diff), while the pre-fix sub-rect re-anchor measures ~7.9 — inside 8.
            double meanDiff = MeanChannelDiff(full, cropped, x0: 45, y0: 40, x1: 120, y1: 85);
            TestContext.WriteLine($"kept-region mean channel diff = {meanDiff:F3} (tolerance 3)");
            Assert.That(meanDiff, Is.LessThanOrEqualTo(3.0),
                "the offset-ROI gradient-brush FlatShadow must anchor its brush to the un-cropped frame (no shift)");
        });
    }

    // InnerShadow draws its input plus an offset, blurred shadow copy, both anchored to the un-cropped OutputBounds
    // origin (identity forward). Round-2 gave it only the backward map; without the origin bridge an OFFSET render
    // ROI still shifts both draws by the crop offset. The pass must bake its content registered to its resolved
    // sub-rect, not to its un-cropped origin.
    [Test]
    public void InnerShadow_KeptRegionUnshiftedUnderOffsetRoi()
    {
        InnerShadow Make() => new()
        {
            Position = { CurrentValue = new Point(12, 8) },
            Sigma = { CurrentValue = new Size(3, 3) },
            Color = { CurrentValue = Colors.Red },
        };

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap full = RenderChain([Make()], ShapeInput, Rect.Invalid);
            using Bitmap cropped = RenderChain([Make()], ShapeInput, new Rect(19, 29, 140, 90));

            double meanDiff = MeanChannelDiff(full, cropped, WinX0, WinY0, WinX1, WinY1);
            TestContext.WriteLine($"kept-region mean channel diff = {meanDiff:F3} (tolerance 8)");
            Assert.That(meanDiff, Is.LessThanOrEqualTo(8.0),
                "the offset-ROI InnerShadow output must register to its sub-rect (no crop-offset shift)");
        });
    }

    // FlatShadow contour-traces the WHOLE materialized input (ContourTracer.FindContours over the snapshot), so its
    // backward must claim the full input regardless of the requested output region — a band r ∪ (r − extrusionVector)
    // is insufficient because a cropped snapshot yields false/truncated contours (StrokeEffect's shape). Re-pinned
    // from the extrusion band to the full input as a deliberate strengthening.
    [Test]
    public void FlatShadow_BackwardRoi_ClaimsFullInput()
    {
        var effect = new FlatShadow
        {
            Angle = { CurrentValue = 0 },
            Length = { CurrentValue = 40 },
        };

        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        effect.Describe(builder, (FilterEffect.Resource)(object)effect.ToResource(CompositionContext.Default));
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);

        var requested = new Rect(50, 40, 40, 30);
        Rect required = plan.Passes[0].BackwardBounds(requested);
        Assert.That(required, Is.EqualTo(s_bounds),
            $"FlatShadow backward({requested}) = {required} must claim the full input {s_bounds} (global contour tracing)");
    }

    // Clipping's backward map: the buffer occupies TargetBounds, but the source is anchored to NewBounds. A fixed clip
    // keeps them equal (identity backward), but AutoCenter re-centers TargetBounds away from NewBounds, so the backward
    // must translate by the centering offset. An identity backward there mis-claims and crops the upstream (A3).
    [Test]
    public void Clipping_Backward_IsIdentityOnlyWhenNotAutoCentered()
    {
        var requested = new Rect(50, 40, 40, 30);
        Rect fixedBackward = ClipBackward(autoCenter: false, requested);
        Rect centeredBackward = ClipBackward(autoCenter: true, requested);

        Assert.Multiple(() =>
        {
            Assert.That(fixedBackward, Is.EqualTo(requested),
                "a fixed clip does not move content, so its backward is identity");
            Assert.That(centeredBackward, Is.Not.EqualTo(requested),
                "AutoCenter re-centers the buffer away from the source anchor, so its backward translates the claim");
            Assert.That(centeredBackward.Size, Is.EqualTo(requested.Size),
                "the backward is a pure translation — a clip moves, never resizes, the required region");
        });
    }

    // The AutoCenter render's origin bridge: a downstream deflating pass (here an offset render-request ROI) crops the
    // clip pass so session.Bounds is an offset sub-rect of TargetBounds. Without the bridge the source anchor stays in
    // the un-cropped TargetBounds frame and the kept region shifts by the crop offset.
    [Test]
    public void Clipping_AutoCenter_KeptRegionUnshiftedUnderOffsetRoi()
    {
        Clipping Make() => new()
        {
            Left = { CurrentValue = 40 },
            AutoCenter = { CurrentValue = true },
        };

        using Bitmap full = RenderChain([Make()], ShapeInput, Rect.Invalid);
        using Bitmap cropped = RenderChain([Make()], ShapeInput, new Rect(40, 10, 80, 100));

        double meanDiff = MeanChannelDiff(full, cropped, 50, 20, 110, 100);
        TestContext.WriteLine($"kept-region mean channel diff = {meanDiff:F3} (tolerance 8)");
        Assert.That(meanDiff, Is.LessThanOrEqualTo(8.0),
            "the offset-ROI AutoCenter clip must register to its sub-rect (no crop-offset shift)");
    }

    private static Rect ClipBackward(bool autoCenter, Rect requested)
    {
        var clip = new Clipping { Left = { CurrentValue = 40 }, AutoCenter = { CurrentValue = autoCenter } };
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f);
        clip.Describe(builder, (FilterEffect.Resource)(object)clip.ToResource(CompositionContext.Default));
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        return plan.Passes[0].BackwardBounds(requested);
    }

    // Renders the effect alone and effect->Clipping{Left,Top}, both onto a fixed black canvas, then asserts the
    // interior of the kept region matches (mean per-channel byte difference under tolerance). A content shift by the
    // crop offset blows this far past tolerance.
    private static void AssertKeptRegionMatchesUncropped(
        FilterEffect effect, double tolerance, Func<RenderNodeOperation> input)
    {
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap uncropped = RenderChain([effect], input, Rect.Invalid);
            using Bitmap cropped = RenderChain([effect, MakeClip()], input, Rect.Invalid);

            double meanDiff = MeanChannelDiff(uncropped, cropped, WinX0, WinY0, WinX1, WinY1);
            TestContext.WriteLine($"kept-region mean channel diff = {meanDiff:F3} (tolerance {tolerance})");
            Assert.That(meanDiff, Is.LessThanOrEqualTo(tolerance),
                "the clipped kept region must match the uncropped render (no crop-offset shift)");
        });
    }

    private static Clipping MakeClip() => new()
    {
        Left = { CurrentValue = ClipLeft },
        Top = { CurrentValue = ClipTop },
    };

    private static Bitmap RenderChain(
        FilterEffect[] effects, Func<RenderNodeOperation> input, Rect requestedBounds, bool clearTransparent = false)
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
            canvas.Clear(clearTransparent ? Colors.Transparent : Colors.Black);
            foreach (RenderNodeOperation op in ops)
                op.Render(canvas);
        }

        RenderNodeOperation.DisposeAll(ops);
        return target.Snapshot();
    }

    // A smooth cross-gradient input: a 1px clip-boundary shift barely perturbs it, while a crop-offset shift does not.
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

    // A sharp off-centre shape for the FlatShadow silhouette; the extruded shadow gives the interior spatial features.
    private static RenderNodeOperation ShapeInput()
    {
        var rect = new Rect(35, 20, 70, 55);
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(rect, Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);
    }

    private static RenderNodeOperation OffsetShapeInput()
    {
        var rect = new Rect(80, 50, 30, 24);
        return RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(rect, Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);
    }

    private static Rect FindAlphaBounds(Bitmap bitmap)
    {
        int left = bitmap.Width;
        int top = bitmap.Height;
        int right = -1;
        int bottom = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.SKBitmap.GetPixel(x, y).Alpha == 0)
                    continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return right < left || bottom < top
            ? default
            : new Rect(left, top, right - left + 1, bottom - top + 1);
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
