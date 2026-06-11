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
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap r1 = GoldenImageHarness.RenderAtScale(Make(makeEffect), Frame, 1f);
            using Bitmap hi = GoldenImageHarness.RenderAtScale(Make(makeEffect), Frame, 2f);
            using Bitmap delivered = GoldenImageHarness.MitchellResampleTo(hi, Frame);
            double ssim = ImageMetrics.Ssim(r1, delivered);
            TestContext.WriteLine($"[{name}] 2x-delivered vs 1:1 SSIM={ssim:F4}");
            Assert.That(ssim, Is.GreaterThan(0.95),
                $"{name}: supersampled diverged from 1:1 — an absolute-px parameter is not scaled by the working density");
        });
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
