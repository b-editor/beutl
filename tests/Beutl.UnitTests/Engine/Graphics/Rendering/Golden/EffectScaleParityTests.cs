using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using static Beutl.UnitTests.Engine.Graphics.Rendering.Golden.GoldenImageHarness;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Every Custom effect with an absolute-length parameter must scale it by w, keeping logical appearance
// across supersample scales. SSIM(1:1, 2x-delivered) > 0.95 = scale-correct.
[NonParallelizable]
[TestFixture]
public class EffectScaleParityTests
{
    private static readonly PixelSize Frame = new(200, 200);
    private const double EffectNonVacuityTolerance = 0.02;

    public static IEnumerable<TestCaseData> Effects()
    {
        yield return new TestCaseData("InnerShadow", (Func<FilterEffect>)(() =>
        {
            var e = new InnerShadow();
            e.Position.Value = new Point(6, 6);
            e.Sigma.Value = new Size(6, 6);
            e.Color.Value = Colors.Black;
            return e;
        }));
        yield return new TestCaseData("ColorShift", (Func<FilterEffect>)(() =>
        {
            var e = new ColorShift();
            e.RedOffset.Value = new PixelPoint(18, 6);
            e.BlueOffset.Value = new PixelPoint(-18, -6);
            return e;
        }));
        yield return new TestCaseData("SplitEffect", (Func<FilterEffect>)(() =>
        {
            var e = new SplitEffect();
            e.HorizontalDivisions.Value = 3;
            e.VerticalDivisions.Value = 3;
            e.HorizontalSpacing.Value = 12;
            e.VerticalSpacing.Value = 12;
            return e;
        }));
        yield return new TestCaseData("FlatShadow", (Func<FilterEffect>)(() =>
        {
            var e = new FlatShadow();
            e.Angle.Value = 0;
            e.Length.Value = 40;
            e.Brush.Value = Brushes.Red;
            return e;
        }));
        yield return new TestCaseData("Clipping", (Func<FilterEffect>)(() =>
        {
            var e = new Clipping();
            e.Left.Value = 24;
            e.Top.Value = 24;
            e.Right.Value = 24;
            e.Bottom.Value = 24;
            return e;
        }));
        yield return new TestCaseData("Clipping-AutoCenter", (Func<FilterEffect>)(() =>
        {
            var e = new Clipping();
            e.Left.Value = 20;
            e.Top.Value = 20;
            e.Right.Value = 20;
            e.Bottom.Value = 20;
            e.AutoCenter.Value = true;
            return e;
        }));
        foreach (float width in new[] { 0f, 36.5f, -36.5f })
        {
            yield return new TestCaseData($"DiagonalClipping-Width{width}",
                (Func<FilterEffect>)(() => MakeDiagonalClipping(width)));
        }
        yield return new TestCaseData("StrokeEffect", (Func<FilterEffect>)(() =>
        {
            var pen = new Pen();
            pen.Thickness.Value = 14;
            pen.Brush.Value = Brushes.Red;
            var e = new StrokeEffect();
            e.Pen.Value = pen;
            return e;
        }));
        yield return new TestCaseData("PathFollow", (Func<FilterEffect>)(() =>
        {
            var e = new PathFollowEffect();
            e.Geometry.Value = PathGeometry.Parse("M0,0 L70,50");
            e.Progress.Value = 100;
            return e;
        }));
        yield return new TestCaseData("StrokeEffect-Offset", (Func<FilterEffect>)(() =>
        {
            // Non-zero offset is the scale-sensitive case the plain StrokeEffect case omits.
            var pen = new Pen();
            pen.Thickness.Value = 14;
            pen.Brush.Value = Brushes.Red;
            var e = new StrokeEffect();
            e.Pen.Value = pen;
            e.Offset.Value = new Point(20, 12);
            return e;
        }));
        yield return new TestCaseData("StrokeEffect-Foreground", (Func<FilterEffect>)(() =>
        {
            // Foreground draw-ordering branch at supersample.
            var pen = new Pen();
            pen.Thickness.Value = 14;
            pen.Brush.Value = Brushes.Red;
            var e = new StrokeEffect();
            e.Pen.Value = pen;
            e.Offset.Value = new Point(16, 16);
            e.Style.Value = StrokeEffect.StrokeStyles.Foreground;
            return e;
        }));
        yield return new TestCaseData("TransformEffect", (Func<FilterEffect>)(() =>
        {
            // Rotate + non-uniform scale: buffer must be placed at working density.
            var group = new TransformGroup();
            var rot = new RotationTransform();
            rot.Rotation.Value = 45f;
            var scale = new ScaleTransform();
            scale.ScaleX.Value = 120f;
            scale.ScaleY.Value = 100f;
            group.Children.Add(rot);
            group.Children.Add(scale);
            var e = new TransformEffect();
            e.Transform.Value = group;
            return e;
        }));
        yield return new TestCaseData("TransformEffect-Filter", (Func<FilterEffect>)(() =>
        {
            // ApplyToTarget == false routes through the Skia matrix ImageFilter (non-buffer branch).
            var group = new TransformGroup();
            var rot = new RotationTransform();
            rot.Rotation.Value = 45f;
            var scale = new ScaleTransform();
            scale.ScaleX.Value = 120f;
            scale.ScaleY.Value = 100f;
            group.Children.Add(rot);
            group.Children.Add(scale);
            var e = new TransformEffect();
            e.Transform.Value = group;
            e.ApplyToTarget.Value = false;
            return e;
        }));
        yield return new TestCaseData("PartsSplit", (Func<FilterEffect>)(() =>
            // Contour readback: the per-target shrink makes every split contour visibly load-bearing.
            MakePartsSplitEffect()));
        yield return new TestCaseData("LayerEffect-AfterSplit", (Func<FilterEffect>)(() =>
        {
            // Split into 9 parts so LayerEffect flattens multiple targets at the working density.
            var split = new SplitEffect();
            split.HorizontalDivisions.Value = 3;
            split.VerticalDivisions.Value = 3;
            split.HorizontalSpacing.Value = 10;
            split.VerticalSpacing.Value = 10;
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
            grad.StartPoint.Value = new RelativePoint(0, 0, RelativeUnit.Absolute);
            grad.EndPoint.Value = new RelativePoint(160, 0, RelativeUnit.Absolute);
            grad.GradientStops.Add(new GradientStop(Colors.Red, 0));
            grad.GradientStops.Add(new GradientStop(Colors.Blue, 1));
            var e = new FlatShadow();
            e.Angle.Value = 0;
            e.Length.Value = 40;
            e.Brush.Value = grad;
            return e;
        }));
        yield return new TestCaseData("FlatShadow-PerlinNoise", (Func<FilterEffect>)(() =>
        {
            // Perlin shadow brush: noise must be in logical space, not device space.
            var noise = new PerlinNoiseBrush();
            noise.BaseFrequencyX.Value = 2f;
            noise.BaseFrequencyY.Value = 2f;
            noise.Octaves.Value = 2;
            noise.Seed.Value = 1f;
            var e = new FlatShadow();
            e.Angle.Value = 35;
            e.Length.Value = 80;
            e.Brush.Value = noise;
            e.ShadowOnly.Value = true;
            return e;
        }));
        yield return new TestCaseData("Mosaic-AbsoluteOrigin", (Func<FilterEffect>)(() =>
        {
            // Absolute-unit Origin must scale by w.
            var e = new MosaicEffect();
            e.TileSize.Value = new Size(16, 16);
            e.Origin.Value = new RelativePoint(50, 30, RelativeUnit.Absolute);
            return e;
        }));
        foreach (TestCaseData testCase in DisplacementMapEffects())
            yield return testCase;
    }

    public static IEnumerable<TestCaseData> DisplacementMapEffects()
    {
        yield return new TestCaseData(
            "DisplacementMap-DrawableMap",
            (Func<FilterEffect>)MakeDrawableDisplacementMapEffect);
    }

    private static FilterEffect MakeDrawableDisplacementMapEffect()
    {
        // Non-gradient (DrawableBrush) displacement map: exercises the tile-brush density path.
        var stripes = new LinearGradientBrush();
        stripes.StartPoint.Value = new RelativePoint(0, 0, RelativeUnit.Absolute);
        stripes.EndPoint.Value = new RelativePoint(24, 0, RelativeUnit.Absolute);
        stripes.SpreadMethod.Value = GradientSpreadMethod.Repeat;
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0));
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0.5f));
        stripes.GradientStops.Add(new GradientStop(Colors.Black, 0.5f));
        stripes.GradientStops.Add(new GradientStop(Colors.Black, 1));
        var mapContent = new RectShape();
        mapContent.AlignmentX.Value = AlignmentX.Center;
        mapContent.AlignmentY.Value = AlignmentY.Center;
        mapContent.Width.Value = 200;
        mapContent.Height.Value = 200;
        mapContent.Fill.Value = stripes;
        var map = new DrawableBrush();
        map.Drawable.Value = mapContent;
        map.Stretch.Value = Stretch.Fill;
        var transform = new DisplacementMapTranslateTransform();
        transform.X.Value = 16;
        transform.Y.Value = 0;
        var effect = new DisplacementMapEffect();
        effect.DisplacementMap.Value = map;
        effect.Transform.Value = transform;
        effect.Channel.Value = DisplacementMapChannel.Luminance;
        return effect;
    }

    private static FilterEffect MakeNoDisplacementMapEffect()
    {
        var effect = new DisplacementMapEffect();
        effect.DisplacementMap.Value = null;
        return effect;
    }

    [TestCaseSource(nameof(DisplacementMapEffects))]
    public void DisplacementMapEffect_ChangesIdentityOutput(string name, Func<FilterEffect> makeEffect)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // A translate displacement over a flat fill is invisible under clamp sampling, so the source
            // carries its own stripes; only a materialized map can move them.
            using Bitmap mapped = GoldenImageHarness.RenderAtScale(
                RotatedRect(makeEffect, MakeStripes(20)),
                Frame,
                1f);
            using Bitmap identity = GoldenImageHarness.RenderAtScale(
                RotatedRect(MakeNoDisplacementMapEffect, MakeStripes(20)),
                Frame,
                1f);

            Assert.That(
                ImageMetrics.FirstNonFinite(("mapped", mapped), ("identity", identity)),
                Is.Null,
                $"{name}: the drawable-map vacuity comparison requires finite renders");
            double mae = ImageMetrics.MeanAbsoluteError(mapped, identity);
            double ssim = ImageMetrics.Ssim(mapped, identity);
            TestContext.WriteLine($"[{name}] mapped vs identity MAE={mae:F4} SSIM={ssim:F4}");
            Assert.That(
                mae,
                Is.GreaterThan(0.001),
                $"{name}: the drawable displacement map did not change the identity render; "
                + "transparent map materialization would make the scale-parity case vacuous");
        });
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
        e.Script.Value = SkslBorderScript;
        return e;
    }

    private static LinearGradientBrush MakeStripes(float period)
    {
        var stripes = new LinearGradientBrush();
        stripes.StartPoint.Value = new RelativePoint(0, 0, RelativeUnit.Absolute);
        stripes.EndPoint.Value = new RelativePoint(period, 0, RelativeUnit.Absolute);
        stripes.SpreadMethod.Value = GradientSpreadMethod.Repeat;
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0));
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0.5f));
        stripes.GradientStops.Add(new GradientStop(Colors.Gray, 0.5f));
        stripes.GradientStops.Add(new GradientStop(Colors.Gray, 1));
        return stripes;
    }

    private static LinearGradientBrush MakeAlphaStripes(float period)
    {
        var stripes = new LinearGradientBrush();
        stripes.StartPoint.Value = new RelativePoint(0, 0, RelativeUnit.Absolute);
        stripes.EndPoint.Value = new RelativePoint(period, 0, RelativeUnit.Absolute);
        stripes.SpreadMethod.Value = GradientSpreadMethod.Repeat;
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0));
        stripes.GradientStops.Add(new GradientStop(Colors.White, 0.45f));
        stripes.GradientStops.Add(new GradientStop(Colors.Transparent, 0.45f));
        stripes.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
        return stripes;
    }

    private static FilterEffect MakePartsSplitEffect()
    {
        var group = new FilterEffectGroup();
        group.Children.Add(new PartsSplitEffect());
        group.Children.Add(MakePartsSplitDifferentiator());
        return group;
    }

    private static TransformEffect MakePartsSplitDifferentiator()
    {
        var scale = new ScaleTransform();
        scale.ScaleX.Value = 85;
        scale.ScaleY.Value = 85;
        var effect = new TransformEffect();
        effect.Transform.Value = scale;
        return effect;
    }

    [TestCaseSource(nameof(Effects))]
    public void Effect_Supersampled_KeepsLogicalAppearance(string name, Func<FilterEffect> makeEffect)
        => AssertSupersampleParity(name, 2f, makeEffect);

    private static void AssertSupersampleParity(string name, float scale, Func<FilterEffect> makeEffect)
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
                using Bitmap r1 = GoldenImageHarness.RenderAtScale(MakeForCase(name, makeEffect), Frame, 1f);
                using Bitmap hi = GoldenImageHarness.RenderAtScale(MakeForCase(name, makeEffect), Frame, scale);
                using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(hi, Frame);
                using Bitmap control = GoldenImageHarness.RenderAtScale(
                    MakeForCase(name, ControlForCase(name)),
                    Frame,
                    1f);

                string? refNonFinite = ImageMetrics.FirstNonFinite(("1:1", r1));
                string? scaledNonFinite = ImageMetrics.FirstNonFinite(
                    ($"{scale}x", hi),
                    ($"{scale}x-delivered", delivered));
                Assert.That(
                    ImageMetrics.FirstNonFinite(("effect-free-control", control)),
                    Is.Null,
                    $"{name}: the effect-free non-vacuity control must be finite");
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
                Assert.That(
                    ImageMetrics.HasFiniteVisibleContent(r1),
                    $"{name}: the 1:1 reference must render finite visible content.");
                double effectDelta = Math.Max(
                    ImageMetrics.MeanAbsoluteError(r1, control),
                    1 - ImageMetrics.Ssim(r1, control));
                Assert.That(
                    effectDelta,
                    Is.GreaterThan(EffectNonVacuityTolerance),
                    $"{name}: the fixture is indistinguishable from its effect-free control.");
                double ssim = ImageMetrics.Ssim(r1, delivered);
                // Windowed SSIM logged as diagnostic only (no universal floor; structural effects can be low).
                double windowed = ImageMetrics.WindowedSsim(r1, delivered, 16);
                TestContext.WriteLine($"[{name}] {scale}x-delivered vs 1:1 SSIM={ssim:F4} windowed-min={windowed:F4}");
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
            bool scaledDeterministic = HasStableNonFiniteLocation(attempts.Select(static item => item.Scaled));
            bool refDeterministic = HasStableNonFiniteLocation(attempts.Select(static item => item.Ref));
            if (refDeterministic)
            {
                Assert.Fail($"{name}: the 1x reference produced a non-finite pixel at the same location on all "
                    + $"{maxAttempts} attempts [{attempts[0].Ref}]; deterministic invalid reference output is a "
                    + "renderer defect, not a run-varying software-Vulkan artifact.");
            }

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
            InconclusiveOnSoftwareVulkan($"{name}: persistent non-finite pixels across {maxAttempts} attempts ["
                + string.Join("; ", attempts.Select(a => $"ref={a.Ref ?? "ok"}|scaled={a.Scaled ?? "ok"}"))
                + $"] — {detail}; parity is verified on a hardware GPU.");
        }
    }

    /// <summary>
    /// Skips a run-varying non-finite result as a software-Vulkan artifact, and fails it on a hardware GPU.
    /// </summary>
    /// <remarks>
    /// Non-finite pixels are exactly what the scratch-memory initialization in the Vulkan backend exists to
    /// prevent. Only a software device such as SwiftShader explains them away, so on a hardware GPU swallowing
    /// them would hide the regression this suite is here to catch.
    /// </remarks>
    private static void InconclusiveOnSoftwareVulkan(string message)
    {
        TestGpu.RequireHardwareGpu(message);
        Assert.Fail(message
            + " The device is a hardware GPU, so a non-finite pixel is a defect, not a software-Vulkan artifact.");
    }

    [TestCase(0f)]
    [TestCase(36.5f)]
    [TestCase(-36.5f)]
    public void DiagonalClipping_PartialRequestPreservesTheBoundary(float width)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var region = new Rect(50, 70, 80, 70);
            using Drawable.Resource resource = RotatedRect(() => MakeDiagonalClipping(width));
            using Bitmap full = RenderAtScale(resource, Frame, 2, clearColor: Colors.Transparent);
            using Bitmap partial = RenderAtScale(resource, Frame, 2, requestedRegion: region, clearColor: Colors.Transparent);
            using Bitmap expected = full.ExtractSubset(PixelRect.FromRect(region, 2));
            using Bitmap actual = partial.ExtractSubset(PixelRect.FromRect(region, 2));
            float[] alpha = expected.GetPixelSpan<ushort>().ToArray()
                .Where((_, index) => index % 4 == 3).Select(bits => (float)BitConverter.UInt16BitsToHalf(bits)).ToArray();
            Assert.Multiple(() =>
            {
                // Changing the device origin affects rotated-source GPU sampling by about 0.004 in alpha.
                Assert.That(ImageMetrics.MaximumAbsoluteErrorPerChannel(expected, actual).Maximum, Is.LessThanOrEqualTo(0.005));
                Assert.That(alpha.Count(value => value > 0.99f), Is.GreaterThan(100));
                Assert.That(alpha.Count(value => value < 0.01f), Is.GreaterThan(100));
            });
        });
    }

    private static DiagonalClipping MakeDiagonalClipping(float width) => new()
    {
        Angle = { Value = 31f },
        Position = { Value = 17.25f },
        Width = { Value = width },
    };

    private static Drawable.Resource MakeForCase(string name, Func<FilterEffect> makeEffect)
    {
        if (name.StartsWith("InnerShadow", StringComparison.Ordinal))
            return MakeInnerShadowFixture(makeEffect);

        Brush fill = name.StartsWith("PartsSplit", StringComparison.Ordinal)
            ? MakeAlphaStripes(60)
            : name.StartsWith("Mosaic-AbsoluteOrigin", StringComparison.Ordinal)
              || name.StartsWith("DisplacementMap-DrawableMap", StringComparison.Ordinal)
                ? MakeStripes(20)
                : Brushes.White;
        return RotatedRect(makeEffect, fill);
    }

    private static Drawable.Resource MakeInnerShadowFixture(Func<FilterEffect> makeEffect)
    {
        // Reuse the finite axis-aligned InnerShadow survey input so this fixture isolates density scaling;
        // transformed-edge rendering is a separate renderer contract.
        var shape = new EllipseShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.TransformOrigin.Value = RelativePoint.Center;
        shape.Width.Value = 150;
        shape.Height.Value = 110;
        shape.Fill.Value = Brushes.White;
        shape.FilterEffect.Value = makeEffect();
        return shape.ToResource(CompositionContext.Default);
    }

    private static Func<FilterEffect> ControlForCase(string name)
        => name.StartsWith("PartsSplit", StringComparison.Ordinal)
            ? MakePartsSplitDifferentiator
            : static () => new FilterEffectGroup();

    public static IEnumerable<TestCaseData> RepresentativeEffectsWithScales()
    {
        float[] scales = [1.5f, 3f];
        string[] names = ["InnerShadow", "StrokeEffect-Offset", "Mosaic-AbsoluteOrigin", "FlatShadow"];
        Dictionary<string, Func<FilterEffect>> effects = Effects().ToDictionary(
            static testCase => (string)testCase.Arguments[0]!,
            static testCase => (Func<FilterEffect>)testCase.Arguments[1]!);
        foreach (float s in scales)
            foreach (string name in names)
                yield return new TestCaseData($"{name}@{s:F1}x", s, effects[name]);
    }

    [TestCaseSource(nameof(RepresentativeEffectsWithScales))]
    public void Effect_Supersampled_AtVariousScales(string label, float scale, Func<FilterEffect> makeEffect)
        => AssertSupersampleParity(label, scale, makeEffect);

    // Vacuity guard: the SKSL border must compile and visibly change the render.
    [Test]
    public void SkslBorderScript_CompilesAndApplies()
    {
        Assert.That(new SKSLScriptEffect().ValidateScript(SkslBorderScript).Error, Is.Null, "SKSL border script must compile");

        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap bordered = GoldenImageHarness.RenderAtScale(RotatedRect(MakeSkslBorderEffect), Frame, 1f);
            // An empty FilterEffectGroup is an identity effect: same fixture, no shader.
            using Bitmap plain = GoldenImageHarness.RenderAtScale(
                RotatedRect(() => new FilterEffectGroup()), Frame, 1f);
            (int redBorderPixels, int preservedInteriorPixels) = CountSkslBorderPixels(bordered);
            double ssim = ImageMetrics.Ssim(bordered, plain);
            TestContext.WriteLine($"[SKSLScript guard] bordered vs plain SSIM={ssim:F4}");
            Assert.Multiple(() =>
            {
                Assert.That(
                    ImageMetrics.FirstNonFinite(("bordered", bordered), ("plain", plain)),
                    Is.Null,
                    "the SKSL vacuity guard requires finite output");
                Assert.That(redBorderPixels, Is.GreaterThan(0),
                    "the SKSL output must contain the expected opaque red border");
                Assert.That(preservedInteriorPixels, Is.GreaterThan(0),
                    "the SKSL output must preserve visible interior source content");
                Assert.That(ssim, Is.LessThan(0.99),
                    "SKSL border did not change the render — the script likely failed to compile/apply, which would make the SKSLScript-Border parity case vacuous");
            });
        });
    }

    private static (int RedBorderPixels, int PreservedInteriorPixels) CountSkslBorderPixels(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int redBorderPixels = 0;
        int preservedInteriorPixels = 0;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            float red = (float)BitConverter.UInt16BitsToHalf(pixels[index]);
            float green = (float)BitConverter.UInt16BitsToHalf(pixels[index + 1]);
            float blue = (float)BitConverter.UInt16BitsToHalf(pixels[index + 2]);
            float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[index + 3]);
            if (alpha > 0.9f && red > 0.9f && green < 0.1f && blue < 0.1f)
                redBorderPixels++;
            if (alpha > 0.9f && red > 0.8f && green > 0.8f && blue > 0.8f)
                preservedInteriorPixels++;
        }
        return (redBorderPixels, preservedInteriorPixels);
    }

    [Test]
    public void NonFiniteClassifier_DistinguishesStableFromRunVaryingLocations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HasStableNonFiniteLocation([
                "1:1 @(4,5) = NaN",
                "1:1 @(4,5) = Infinity",
                "1:1 @(4,5) = NaN",
            ]), Is.True);
            Assert.That(HasStableNonFiniteLocation([
                "1:1 @(4,5) = NaN",
                "1:1 @(6,5) = NaN",
                "1:1 @(4,5) = NaN",
            ]), Is.False);
            Assert.That(HasStableNonFiniteLocation(["1:1 @(4,5) = NaN", null, null]), Is.False);
        });
    }

    private static bool HasStableNonFiniteLocation(IEnumerable<string?> samples)
    {
        string?[] values = samples.ToArray();
        return values.Length > 0
               && values.All(static value => value is not null)
               && values.Select(static value => value!.Split(" = ", StringSplitOptions.None)[0])
                   .Distinct(StringComparer.Ordinal)
                   .Count() == 1;
    }
}
