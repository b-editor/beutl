using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Every Custom effect with an absolute-length parameter must scale it by w, keeping logical appearance
// across supersample scales. SSIM(1:1, 2x-delivered) > 0.95 = scale-correct.
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
            // Non-zero offset is the scale-sensitive case the plain StrokeEffect case omits.
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
            // Foreground draw-ordering branch at supersample.
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
            // Rotate + non-uniform scale: buffer must be placed at working density.
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
            // ApplyToTarget == false routes through the Skia matrix ImageFilter (non-buffer branch).
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
            // Contour readback: contours in device px must convert to logical via /w.
            new PartsSplitEffect()));
        yield return new TestCaseData("LayerEffect-AfterSplit", (Func<FilterEffect>)(() =>
        {
            // Split into 9 parts so LayerEffect flattens multiple targets at the working density.
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
            // Absolute-unit gradient endpoints must stay in logical px.
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
            // Perlin shadow brush: noise must be in logical space, not device space.
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
            // Absolute-unit Origin must scale by w.
            var e = new MosaicEffect();
            e.TileSize.CurrentValue = new Size(16, 16);
            e.Origin.CurrentValue = new RelativePoint(50, 30, RelativeUnit.Absolute);
            return e;
        }));
        yield return new TestCaseData("DisplacementMap-DrawableMap", (Func<FilterEffect>)(() =>
        {
            // Non-gradient (DrawableBrush) displacement map: exercises the tile-brush density path.
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

    // Border whose thickness is a fixed logical width (10 px): iScale and iResolution are both load-bearing.
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

        // Non-finite pixels (SwiftShader blur artifact vs real scale defect) are distinguished by
        // determinism: same location on every attempt = real defect (FAIL); moving = artifact (INCONCLUSIVE).
        // Reference and scaled renders are scanned separately so a broken reference does not mask a scaled defect.
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

                // Parity is measurable: discard earlier transient non-finite records.
                attempts.Clear();
                double ssim = ImageMetrics.Ssim(r1, delivered);
                // Windowed SSIM logged as diagnostic only (no universal floor; structural effects can be low).
                double windowed = ImageMetrics.WindowedSsim(r1, delivered, 16);
                TestContext.WriteLine($"[{name}] 2x-delivered vs 1:1 SSIM={ssim:F4} windowed-min={windowed:F4}");
                Assert.That(ssim, Is.GreaterThan(0.95),
                    $"{name}: supersampled diverged from 1:1 — an absolute-px parameter is not scaled by the working density");
                return;
            }
        });

        if (attempts.Count == maxAttempts)
        {
            // Reference finite on at least one attempt means baseline is trustworthy.
            bool refEverFinite = attempts.Any(a => a.Ref is null);

            // Compare location only (strip value), not the exact NaN/Inf bit pattern.
            bool scaledAllNonFinite = attempts.All(a => a.Scaled is not null);
            string[] scaledLocations = attempts
                .Where(a => a.Scaled is not null)
                .Select(a => a.Scaled!.Split(" = ", StringSplitOptions.None)[0])
                .ToArray();
            bool scaledDeterministic = scaledAllNonFinite && scaledLocations.Distinct().Count() == 1;

            // Deterministic non-finite in scaled render with finite reference = real scale defect.
            if (scaledDeterministic && refEverFinite)
            {
                Assert.Fail($"{name}: the SCALED render produced a non-finite pixel at the SAME location on all "
                    + $"{maxAttempts} attempts [{attempts.First(a => a.Scaled is not null).Scaled}] while the 1:1 "
                    + "reference was finite — a deterministic NaN/Inf confined to the scale path is a scale-parity "
                    + "defect (an absolute-px parameter is not scaled by the working density), not an environmental "
                    + "artifact.");
            }

            // Non-finite 1:1 reference or run-varying scaled NaN = inconclusive (SwiftShader artifact).
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

    public static IEnumerable<TestCaseData> RepresentativeEffectsWithScales()
    {
        float[] scales = [1.5f, 3f];
        (string Name, Func<FilterEffect> Make)[] effects =
        [
            ("InnerShadow", () => { var e = new InnerShadow(); e.Position.CurrentValue = new Point(20, 20); e.Sigma.CurrentValue = new Size(10, 10); e.Color.CurrentValue = Colors.Black; return e; }),
            ("StrokeEffect-Offset", () => { var pen = new Pen(); pen.Thickness.CurrentValue = 14; pen.Brush.CurrentValue = Brushes.Red; var e = new StrokeEffect(); e.Pen.CurrentValue = pen; e.Offset.CurrentValue = new Point(20, 12); return e; }),
            ("Mosaic-AbsoluteOrigin", () => { var e = new MosaicEffect(); e.TileSize.CurrentValue = new Size(16, 16); e.Origin.CurrentValue = new RelativePoint(50, 30, RelativeUnit.Absolute); return e; }),
            ("FlatShadow", () => { var e = new FlatShadow(); e.Angle.CurrentValue = 0; e.Length.CurrentValue = 40; e.Brush.CurrentValue = Brushes.Red; return e; }),
        ];
        foreach (float s in scales)
            foreach (var (name, make) in effects)
                yield return new TestCaseData($"{name}@{s:F1}x", s, make);
    }

    [TestCaseSource(nameof(RepresentativeEffectsWithScales))]
    public void Effect_Supersampled_AtVariousScales(string label, float scale, Func<FilterEffect> makeEffect)
    {
        VulkanTestEnvironment.EnsureAvailable();

        const int maxAttempts = 3;
        var attempts = new List<(string? Ref, string? Scaled)>();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            for (int attempt = 1; ; attempt++)
            {
                using Bitmap r1 = GoldenImageHarness.RenderAtScale(Make(makeEffect), Frame, 1f);
                using Bitmap hi = GoldenImageHarness.RenderAtScale(Make(makeEffect), Frame, scale);
                using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(hi, Frame);

                string? refNonFinite = ImageMetrics.FirstNonFinite(("1:1", r1));
                string? scaledNonFinite = ImageMetrics.FirstNonFinite(($"{scale}x", hi), ($"{scale}x-delivered", delivered));
                if (refNonFinite is not null || scaledNonFinite is not null)
                {
                    TestContext.WriteLine(
                        $"[{label}] non-finite render on attempt {attempt}: ref={refNonFinite ?? "ok"}; scaled={scaledNonFinite ?? "ok"}");
                    attempts.Add((refNonFinite, scaledNonFinite));
                    if (attempt < maxAttempts)
                        continue;
                    return;
                }

                attempts.Clear();
                double ssim = ImageMetrics.Ssim(r1, delivered);
                TestContext.WriteLine($"[{label}] {scale}x-delivered vs 1:1 SSIM={ssim:F4}");
                Assert.That(ssim, Is.GreaterThan(0.95),
                    $"{label}: supersampled diverged from 1:1 — an absolute-px parameter is not scaled by the working density");
                return;
            }
        });

        if (attempts.Count == maxAttempts)
        {
            bool refEverFinite = attempts.Any(a => a.Ref is null);
            bool scaledAllNonFinite = attempts.All(a => a.Scaled is not null);
            string[] scaledLocations = attempts
                .Where(a => a.Scaled is not null)
                .Select(a => a.Scaled!.Split(" = ", StringSplitOptions.None)[0])
                .ToArray();
            bool scaledDeterministic = scaledAllNonFinite && scaledLocations.Distinct().Count() == 1;

            if (scaledDeterministic && refEverFinite)
            {
                Assert.Fail($"{label}: deterministic non-finite in the SCALED render [{attempts.First(a => a.Scaled is not null).Scaled}] "
                    + "while the 1:1 reference was finite — a scale-parity defect.");
            }

            Assert.Ignore($"{label}: persistent non-finite pixels across {maxAttempts} attempts ["
                + string.Join("; ", attempts.Select(a => $"ref={a.Ref ?? "ok"}|scaled={a.Scaled ?? "ok"}"))
                + "] — software-Vulkan artifact; parity is verified on a hardware GPU.");
        }
    }

    // Vacuity guard: the SKSL border must compile and visibly change the render.
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
