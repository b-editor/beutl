using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// feature 003: every render-target ("Custom") effect with an absolute-length pixel parameter must scale that
// parameter by the working density, so the supersampled (s_out = 2) result keeps the SAME logical appearance as
// the 1:1 render. SSIM(1:1, 2x-delivered) > 0.95 = scale-correct; a large drop = an un-scaled device-density
// parameter (the DisplacementMap-class bug). White-filled rotated rect: the effect operates on the rect's edges /
// interior tiling, which is high-frequency enough to expose a wrong-density parameter.
[NonParallelizable]
[TestFixture]
public class EffectScaleParityTests
{
    private static readonly PixelSize Frame = new(200, 200);

    public static IEnumerable<TestCaseData> Effects()
    {
        yield return new TestCaseData("InnerShadow", (Func<FilterEffect>)(() =>
        {
            var e = new InnerShadow();
            e.Position.CurrentValue = new Point(20, 20);
            e.Sigma.CurrentValue = new Size(10, 10);
            e.Color.CurrentValue = Colors.Black;
            return e;
        }));
        yield return new TestCaseData("ColorShift", (Func<FilterEffect>)(() =>
        {
            var e = new ColorShift();
            e.RedOffset.CurrentValue = new PixelPoint(18, 6);
            e.BlueOffset.CurrentValue = new PixelPoint(-18, -6);
            return e;
        }));
        yield return new TestCaseData("SplitEffect", (Func<FilterEffect>)(() =>
        {
            var e = new SplitEffect();
            e.HorizontalDivisions.CurrentValue = 3;
            e.VerticalDivisions.CurrentValue = 3;
            e.HorizontalSpacing.CurrentValue = 12;
            e.VerticalSpacing.CurrentValue = 12;
            return e;
        }));
        yield return new TestCaseData("FlatShadow", (Func<FilterEffect>)(() =>
        {
            var e = new FlatShadow();
            e.Angle.CurrentValue = 0;
            e.Length.CurrentValue = 40;
            e.Brush.CurrentValue = Brushes.Red;
            return e;
        }));
        yield return new TestCaseData("Clipping", (Func<FilterEffect>)(() =>
        {
            var e = new Clipping();
            e.Left.CurrentValue = 24;
            e.Top.CurrentValue = 24;
            e.Right.CurrentValue = 24;
            e.Bottom.CurrentValue = 24;
            return e;
        }));
        yield return new TestCaseData("Clipping-AutoCenter", (Func<FilterEffect>)(() =>
        {
            var e = new Clipping();
            e.Left.CurrentValue = 20;
            e.Top.CurrentValue = 20;
            e.Right.CurrentValue = 20;
            e.Bottom.CurrentValue = 20;
            e.AutoCenter.CurrentValue = true;
            return e;
        }));
        yield return new TestCaseData("StrokeEffect", (Func<FilterEffect>)(() =>
        {
            var pen = new Pen();
            pen.Thickness.CurrentValue = 14;
            pen.Brush.CurrentValue = Brushes.Red;
            var e = new StrokeEffect();
            e.Pen.CurrentValue = pen;
            return e;
        }));
        yield return new TestCaseData("PathFollow", (Func<FilterEffect>)(() =>
        {
            var e = new PathFollowEffect();
            e.Geometry.CurrentValue = PathGeometry.Parse("M0,0 L70,50");
            e.Progress.CurrentValue = 100;
            return e;
        }));
        yield return new TestCaseData("StrokeEffect-Offset", (Func<FilterEffect>)(() =>
        {
            // The absolute-length Offset must ride the working density (it is applied inside the w-prescaled
            // canvas); a non-zero offset is the scale-sensitive case the plain StrokeEffect case omits.
            var pen = new Pen();
            pen.Thickness.CurrentValue = 14;
            pen.Brush.CurrentValue = Brushes.Red;
            var e = new StrokeEffect();
            e.Pen.CurrentValue = pen;
            e.Offset.CurrentValue = new Point(20, 12);
            return e;
        }));
        yield return new TestCaseData("StrokeEffect-Foreground", (Func<FilterEffect>)(() =>
        {
            // Foreground draw-ordering branch (source drawn AFTER the stroke) at supersample.
            var pen = new Pen();
            pen.Thickness.CurrentValue = 14;
            pen.Brush.CurrentValue = Brushes.Red;
            var e = new StrokeEffect();
            e.Pen.CurrentValue = pen;
            e.Offset.CurrentValue = new Point(16, 16);
            e.Style.CurrentValue = StrokeEffect.StrokeStyles.Foreground;
            return e;
        }));
        yield return new TestCaseData("TransformEffect", (Func<FilterEffect>)(() =>
        {
            // Rotate + non-uniform scale: the rotated buffer must be placed at the working density, not at
            // logical size on a w× device buffer (which shifted it off-position and clipped it before).
            var group = new TransformGroup();
            var rot = new RotationTransform();
            rot.Rotation.CurrentValue = 45f;
            var scale = new ScaleTransform();
            scale.ScaleX.CurrentValue = 120f;
            scale.ScaleY.CurrentValue = 100f;
            group.Children.Add(rot);
            group.Children.Add(scale);
            var e = new TransformEffect();
            e.Transform.CurrentValue = group;
            return e;
        }));
        yield return new TestCaseData("TransformEffect-Filter", (Func<FilterEffect>)(() =>
        {
            // ApplyToTarget == false routes through the Skia matrix ImageFilter (rides the root CTM, needs no
            // ×w). This guards that the non-buffer branch also keeps its logical appearance under supersampling.
            var group = new TransformGroup();
            var rot = new RotationTransform();
            rot.Rotation.CurrentValue = 45f;
            var scale = new ScaleTransform();
            scale.ScaleX.CurrentValue = 120f;
            scale.ScaleY.CurrentValue = 100f;
            group.Children.Add(rot);
            group.Children.Add(scale);
            var e = new TransformEffect();
            e.Transform.CurrentValue = group;
            e.ApplyToTarget.CurrentValue = false;
            return e;
        }));
        yield return new TestCaseData("PartsSplit", (Func<FilterEffect>)(() =>
            // Contour readback path: contours are traced in DEVICE px and the per-part bounds must come back
            // to LOGICAL via ÷w before CreateTarget re-densifies them. No other case covers that branch.
            new PartsSplitEffect()));
        yield return new TestCaseData("LayerEffect-AfterSplit", (Func<FilterEffect>)(() =>
        {
            // Split into 9 parts first so LayerEffect flattens MULTIPLE targets: covers both the Scale(w)
            // prescale branch and the logical per-target placement (t.Bounds.Position - bounds.Position).
            var split = new SplitEffect();
            split.HorizontalDivisions.CurrentValue = 3;
            split.VerticalDivisions.CurrentValue = 3;
            split.HorizontalSpacing.CurrentValue = 10;
            split.VerticalSpacing.CurrentValue = 10;
            var group = new FilterEffectGroup();
            group.Children.Add(split);
            group.Children.Add(new LayerEffect());
            return group;
        }));
        yield return new TestCaseData("SKSLScript-Border", (Func<FilterEffect>)(MakeSkslBorderEffect));
        yield return new TestCaseData("FlatShadow-AbsoluteGradient", (Func<FilterEffect>)(() =>
        {
            // Absolute-unit gradient endpoints in the shadow brush: before the FlatShadow brush fix these were
            // interpreted as DEVICE px (gradient compressed by 1/w at w != 1). 160 logical px must stay 160.
            var grad = new LinearGradientBrush();
            grad.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Absolute);
            grad.EndPoint.CurrentValue = new RelativePoint(160, 0, RelativeUnit.Absolute);
            grad.GradientStops.Add(new GradientStop(Colors.Red, 0));
            grad.GradientStops.Add(new GradientStop(Colors.Blue, 1));
            var e = new FlatShadow();
            e.Angle.CurrentValue = 0;
            e.Length.CurrentValue = 40;
            e.Brush.CurrentValue = grad;
            return e;
        }));
        yield return new TestCaseData("FlatShadow-PerlinNoise", (Func<FilterEffect>)(() =>
        {
            // Perlin shadow brush: before the fix the noise was generated in DEVICE space (w× finer than the
            // same brush on the main canvas). BaseFrequency 2 → 50-logical-px period, far from Nyquist at 1x,
            // so the 2x parity comparison is not confounded by downsampling loss.
            var noise = new PerlinNoiseBrush();
            noise.BaseFrequencyX.CurrentValue = 2f;
            noise.BaseFrequencyY.CurrentValue = 2f;
            noise.Octaves.CurrentValue = 2;
            noise.Seed.CurrentValue = 1f;
            var e = new FlatShadow();
            e.Angle.CurrentValue = 0;
            e.Length.CurrentValue = 40;
            e.Brush.CurrentValue = noise;
            return e;
        }));
        yield return new TestCaseData("Mosaic-AbsoluteOrigin", (Func<FilterEffect>)(() =>
        {
            // Absolute-unit Origin: a LOGICAL anchor point that must scale by w (RelativePoint.ToPixels passes
            // Absolute values through unchanged). Before the fix the grid anchor landed at 1/w of the intended
            // position at w != 1, shifting every block boundary along the rect's edges.
            var e = new MosaicEffect();
            e.TileSize.CurrentValue = new Size(16, 16);
            e.Origin.CurrentValue = new RelativePoint(50, 30, RelativeUnit.Absolute);
            return e;
        }));
        yield return new TestCaseData("DisplacementMap-DrawableMap", (Func<FilterEffect>)(() =>
        {
            // Non-gradient (DrawableBrush) displacement map: exercises the tile-brush density path of the map
            // child shader (rasterize at w + Scale(1/w) compensation composed with the Scale(w) wrapper).
            // Mostly coverage: the wrapper already fixes the POSITION, so the residual density defect is soft
            // map edges, a weaker SSIM signal than the FlatShadow / Mosaic cases.
            var stripes = new LinearGradientBrush();
            stripes.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Absolute);
            stripes.EndPoint.CurrentValue = new RelativePoint(24, 0, RelativeUnit.Absolute);
            stripes.SpreadMethod.CurrentValue = GradientSpreadMethod.Repeat;
            stripes.GradientStops.Add(new GradientStop(Colors.White, 0));
            stripes.GradientStops.Add(new GradientStop(Colors.White, 0.5f));
            stripes.GradientStops.Add(new GradientStop(Colors.Black, 0.5f));
            stripes.GradientStops.Add(new GradientStop(Colors.Black, 1));
            var mapContent = new RectShape();
            mapContent.AlignmentX.CurrentValue = AlignmentX.Center;
            mapContent.AlignmentY.CurrentValue = AlignmentY.Center;
            mapContent.Width.CurrentValue = 200;
            mapContent.Height.CurrentValue = 200;
            mapContent.Fill.CurrentValue = stripes;
            var map = new DrawableBrush();
            map.Drawable.CurrentValue = mapContent;
            map.Stretch.CurrentValue = Stretch.Fill;
            var transform = new DisplacementMapTranslateTransform();
            transform.X.CurrentValue = 16;
            transform.Y.CurrentValue = 0;
            var e = new DisplacementMapEffect();
            e.DisplacementMap.CurrentValue = map;
            e.Transform.CurrentValue = transform;
            e.Channel.CurrentValue = DisplacementMapChannel.Luminance;
            return e;
        }));
    }

    // Border whose thickness is a FIXED LOGICAL width (10 px): iScale converts it to device px and
    // iResolution anchors the right/bottom edges — both uniforms are load-bearing, so an un-×w'd
    // iResolution or a missing iScale shifts/thins the border and breaks 2x parity.
    private const string SkslBorderScript =
        """
        uniform shader src;
        uniform float2 iResolution;
        uniform float iScale;

        half4 main(float2 fragCoord) {
            half4 c = src.eval(fragCoord);
            float border = 10.0 * iScale;
            if (fragCoord.x < border || fragCoord.y < border ||
                fragCoord.x >= iResolution.x - border || fragCoord.y >= iResolution.y - border) {
                return half4(1.0, 0.0, 0.0, 1.0);
            }
            return c;
        }
        """;

    private static FilterEffect MakeSkslBorderEffect()
    {
        var e = new SKSLScriptEffect();
        e.Script.CurrentValue = SkslBorderScript;
        return e;
    }

    private static Drawable.Resource Make(Func<FilterEffect> makeEffect)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        shape.Width.CurrentValue = 140;
        shape.Height.CurrentValue = 90;
        shape.Fill.CurrentValue = Brushes.White;
        var rotation = new RotationTransform();
        rotation.Rotation.CurrentValue = 21f;
        shape.Transform.CurrentValue = rotation;
        shape.FilterEffect.CurrentValue = makeEffect();
        return shape.ToResource(CompositionContext.Default);
    }

    [TestCaseSource(nameof(Effects))]
    public void Effect_Supersampled_KeepsLogicalAppearance(string name, Func<FilterEffect> makeEffect)
    {
        VulkanTestEnvironment.EnsureAvailable();

        // A non-finite (NaN/±Inf) pixel makes SSIM NaN, which would assert as a misleading "scale diverged"
        // failure even though parity was never measured. Two distinct causes produce non-finite output, and
        // they must be told apart by DETERMINISM — not by non-finiteness alone:
        //   * a software-Vulkan (SwiftShader) blur artifact (heaviest case: InnerShadow's sigma×w blur) lands on
        //     a DIFFERENT, run-varying pixel each render — nondeterministic GPU garbage, NOT a scale defect; vs
        //   * a genuine scale-parity defect (an absolute-px parameter left unscaled by the working density) is
        //     DETERMINISTIC — it corrupts the SAME pixel(s) every render, and may itself emit NaN/Inf.
        // So re-render a few times and compare WHERE the non-finite pixel lands across attempts: a location that
        // stays identical on every attempt is deterministic and a real defect → FAIL; a location that moves is
        // the environmental artifact → treat as INCONCLUSIVE (parity is verified on a hardware GPU, e.g.
        // MoltenVK, where the blur is finite) rather than failing the build on it. Gating on non-finiteness
        // alone would silently Ignore a deterministic NaN-producing regression. Assert.Fail / Assert.Ignore are
        // raised on the test thread (not inside InvokeOnRenderThread, where an IgnoreException would be wrapped
        // in an AggregateException and surface as an error instead of an ignore).
        //
        // feature 003 (I8 de-masking fix): scan the SCALED renders (2x / 2x-delivered) and the 1:1 REFERENCE
        // SEPARATELY, not as one ordered FirstNonFinite list. The old single-list scan returned the FIRST
        // non-finite in (1:1, 2x, 2x-delivered) order, so a 1:1-reference artifact (which is common for the
        // InnerShadow blur on CI SwiftShader) MASKED a genuine, simultaneous scale-parity NaN in the 2x render —
        // the gate then Ignored a real defect. Tracking them apart lets a deterministic scaled NaN FAIL whenever
        // the reference was finite (so the comparison's baseline is trustworthy), while a broken reference is
        // still treated as inconclusive.
        const int maxAttempts = 3;
        var attempts = new List<(string? Ref, string? Scaled)>();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            for (int attempt = 1; ; attempt++)
            {
                using Bitmap r1 = GoldenImageHarness.RenderAtScale(Make(makeEffect), Frame, 1f);
                using Bitmap hi = GoldenImageHarness.RenderAtScale(Make(makeEffect), Frame, 2f);
                using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(hi, Frame);

                string? refNonFinite = ImageMetrics.FirstNonFinite(("1:1", r1));
                string? scaledNonFinite = ImageMetrics.FirstNonFinite(("2x", hi), ("2x-delivered", delivered));
                if (refNonFinite is not null || scaledNonFinite is not null)
                {
                    TestContext.WriteLine(
                        $"[{name}] non-finite render on attempt {attempt}: ref={refNonFinite ?? "ok"}; scaled={scaledNonFinite ?? "ok"}");
                    attempts.Add((refNonFinite, scaledNonFinite));
                    if (attempt < maxAttempts)
                        continue;

                    return;
                }

                // Parity is measurable on this attempt: discard any earlier transient non-finite records so the
                // post-closure verdict cannot misread a recovered run as a persistent one.
                attempts.Clear();
                double ssim = ImageMetrics.Ssim(r1, delivered);
                // Log the windowed (min-tile) SSIM as a DIAGNOSTIC alongside the global gate. A hard min-tile
                // floor is intentionally NOT asserted here: an effect with legitimate hard structural boundaries
                // (e.g. PartsSplit/Split) leaves a correct supersample→downsample render with a low worst-tile
                // SSIM (~0.47 observed) even when the global SSIM is ~0.997, so a universal floor false-fails.
                // The windowed metric's discriminating power is locked in by ImageMetricsTests instead; this line
                // records per-effect worst-tile values so a maintainer can set per-effect baselines on hardware.
                double windowed = ImageMetrics.WindowedSsim(r1, delivered, 16);
                TestContext.WriteLine($"[{name}] 2x-delivered vs 1:1 SSIM={ssim:F4} windowed-min={windowed:F4}");
                Assert.That(ssim, Is.GreaterThan(0.95),
                    $"{name}: supersampled diverged from 1:1 — an absolute-px parameter is not scaled by the working density");
                return;
            }
        });

        if (attempts.Count == maxAttempts)
        {
            // The reference was finite on at least one attempt → the comparison baseline is trustworthy there, so
            // a persistent scaled NaN cannot be dismissed as "the reference is broken".
            bool refEverFinite = attempts.Any(a => a.Ref is null);

            // Strip the trailing "= {value}" so we compare WHERE the non-finite component landed (label + x/y/c),
            // not the exact NaN/Inf bit pattern — which can differ even at the same pixel.
            bool scaledAllNonFinite = attempts.All(a => a.Scaled is not null);
            string[] scaledLocations = attempts
                .Where(a => a.Scaled is not null)
                .Select(a => a.Scaled!.Split(" = ", StringSplitOptions.None)[0])
                .ToArray();
            bool scaledDeterministic = scaledAllNonFinite && scaledLocations.Distinct().Count() == 1;

            // A deterministic non-finite confined to the SCALED render, with a FINITE 1:1 reference, is a genuine
            // scale-parity defect (an absolute-px parameter the scale path mis-handled) and is no longer maskable
            // by a simultaneous reference artifact. That fails hard.
            if (scaledDeterministic && refEverFinite)
            {
                Assert.Fail($"{name}: the SCALED render produced a non-finite pixel at the SAME location on all "
                    + $"{maxAttempts} attempts [{attempts.First(a => a.Scaled is not null).Scaled}] while the 1:1 "
                    + "reference was finite — a deterministic NaN/Inf confined to the scale path is a scale-parity "
                    + "defect (an absolute-px parameter is not scaled by the working density), not an environmental "
                    + "artifact.");
            }

            // A non-finite 1:1 (w=1) REFERENCE is, BY CONSTRUCTION, not a scale-parity defect: w=1 is the
            // byte-identity baseline (every scale code path is a no-op there), so a NaN there means the
            // comparison's own reference is broken — a software-Vulkan (SwiftShader) blur artifact (empirically
            // it appears only inside the full GPU suite on the CI amd64 runner, never on arm64/MoltenVK). Treat
            // both that and a run-varying scaled NaN as inconclusive — parity is verified on a hardware GPU.
            bool refBroken = attempts.All(a => a.Ref is not null);
            string detail = refBroken
                ? "the 1:1 (w=1) reference is non-finite on every attempt — the byte-identity baseline is broken "
                  + "by a software-Vulkan (SwiftShader) blur artifact, so parity cannot be measured against it"
                : "a non-finite pixel persisted at a run-varying location — a software-Vulkan (SwiftShader) blur "
                  + "artifact (nondeterministic)";
            Assert.Ignore($"{name}: persistent non-finite pixels across {maxAttempts} attempts ["
                + string.Join("; ", attempts.Select(a => $"ref={a.Ref ?? "ok"}|scaled={a.Scaled ?? "ok"}"))
                + $"] — {detail}; parity is verified on a hardware GPU.");
        }
    }

    // Vacuity guard for the SKSLScript-Border parity case: if the script silently failed to compile, the
    // effect would no-op and the parity test would pass without testing anything. Require that the script
    // compiles and that the bordered render visibly differs from an effect-free render.
    [Test]
    public void SkslBorderScript_CompilesAndApplies()
    {
        Assert.That(SKSLScriptEffect.ValidateScript(SkslBorderScript), Is.Null, "SKSL border script must compile");

        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap bordered = GoldenImageHarness.RenderAtScale(Make(MakeSkslBorderEffect), Frame, 1f);
            // An empty FilterEffectGroup is an identity effect: same fixture, no shader.
            using Bitmap plain = GoldenImageHarness.RenderAtScale(Make(() => new FilterEffectGroup()), Frame, 1f);
            double ssim = ImageMetrics.Ssim(bordered, plain);
            TestContext.WriteLine($"[SKSLScript guard] bordered vs plain SSIM={ssim:F4}");
            Assert.That(ssim, Is.LessThan(0.99),
                "SKSL border did not change the render — the script likely failed to compile/apply, which would make the SKSLScript-Border parity case vacuous");
        });
    }
}
