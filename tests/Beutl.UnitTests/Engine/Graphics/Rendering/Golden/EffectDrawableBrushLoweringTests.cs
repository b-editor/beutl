using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

// Every effect that paints with a user-settable brush or pen must register it so nested DrawableBrush content is
// lowered into the recorded graph. A registered drawable brush whose content is a flat colour must therefore
// render like the equivalent solid brush, and must not render like an absent brush.
[NonParallelizable]
[TestFixture]
public class EffectDrawableBrushLoweringTests
{
    private static readonly PixelSize Frame = new(200, 200);

    private const double EquivalenceTolerance = 0.02;

    public static IEnumerable<TestCaseData> Effects()
    {
        yield return new TestCaseData(
            "FlatShadow",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new FlatShadow();
                effect.Angle.Value = 0;
                effect.Length.Value = 40;
                effect.Brush.Value = brush;
                return effect;
            }));
        yield return new TestCaseData(
            "BlendEffect",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new BlendEffect();
                effect.Brush.Value = brush;
                effect.BlendMode.Value = BlendMode.SrcIn;
                return effect;
            }));
        yield return new TestCaseData(
            "StrokeEffect",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new StrokeEffect();
                if (brush is not null)
                {
                    var pen = new Pen();
                    pen.Thickness.Value = 14;
                    pen.Brush.Value = brush;
                    effect.Pen.Value = pen;
                }

                return effect;
            }));
        yield return new TestCaseData(
            "NestedActivate",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new NestedActivateBrushEffect();
                effect.Brush.Value = brush;
                return effect;
            }));
        yield return new TestCaseData(
            "TypedOperationBeforeBrush",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new TypedOperationBeforeBrushEffect();
                effect.Brush.Value = brush;
                return effect;
            }));
        yield return new TestCaseData(
            "DisplacementMap-ShowMap",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var effect = new DisplacementMapEffect();
                effect.DisplacementMap.Value = brush;
                effect.ShowDisplacementMap.Value = true;
                return effect;
            }));
        // A drawable map takes the effect-item custom-effect path while every other brush takes the shader
        // description, so this case pins both the lowering and the equivalence of the two paths.
        yield return new TestCaseData(
            "DisplacementMap-Transform",
            (Func<Brush?, FilterEffect>)(brush =>
            {
                var transform = new DisplacementMapTranslateTransform();
                transform.X.Value = 24;
                transform.Y.Value = -16;
                var effect = new DisplacementMapEffect();
                effect.DisplacementMap.Value = brush;
                effect.Transform.Value = transform;
                // The source fill is flat, so a uniform displacement only shows where the shifted sampling
                // leaves the source; clamp sampling would reproduce the unshifted fill instead.
                effect.SpreadMethod.Value = GradientSpreadMethod.Decal;
                return effect;
            }));
    }

    // DelayAnimationEffect re-applies its child effect from an execution-time callback, where the recorder is
    // no longer reachable. Its child's brush content must therefore be lowered while the parent is recorded.
    public static IEnumerable<TestCaseData> DelayedEffects()
    {
        foreach (TestCaseData data in Effects())
        {
            var name = (string)data.Arguments[0]!;
            var makeEffect = (Func<Brush?, FilterEffect>)data.Arguments[1]!;
            yield return new TestCaseData(
                $"DelayAnimation-{name}",
                (Func<Brush?, FilterEffect>)(brush =>
                {
                    var group = new FilterEffectGroup();
                    group.Children.Add(makeEffect(brush));
                    var delay = new DelayAnimationEffect();
                    delay.Effect.Value = group;
                    return delay;
                }));
        }
    }

    [TestCaseSource(nameof(Effects))]
    [TestCaseSource(nameof(DelayedEffects))]
    public void EffectOwnedDrawableBrush_RendersLikeTheEquivalentSolidBrush(
        string name,
        Func<Brush?, FilterEffect> makeEffect)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap solid = GoldenImageHarness.RenderAtScale(
                Make(() => makeEffect(MakeSolid())),
                Frame,
                1f);
            using Bitmap drawable = GoldenImageHarness.RenderAtScale(
                Make(() => makeEffect(MakeDrawableBrush())),
                Frame,
                1f);
            using Bitmap absent = GoldenImageHarness.RenderAtScale(
                Make(() => makeEffect(null)),
                Frame,
                1f);

            Assert.That(
                ImageMetrics.FirstNonFinite(
                    ("solid", solid),
                    ("drawable", drawable),
                    ("absent", absent)),
                Is.Null,
                $"{name}: the drawable-brush comparison requires finite renders");

            double equivalence = ImageMetrics.MeanAbsoluteError(solid, drawable);
            double vacuity = ImageMetrics.MeanAbsoluteError(absent, drawable);
            TestContext.WriteLine(
                $"[{name}] drawable vs solid MAE={equivalence:F4}, drawable vs absent MAE={vacuity:F4}");

            Assert.That(
                vacuity,
                Is.GreaterThan(0.001),
                $"{name}: the effect-owned DrawableBrush rendered like an absent brush, so its nested content "
                + "was never materialized");
            Assert.That(
                equivalence,
                Is.LessThan(EquivalenceTolerance),
                $"{name}: the effect-owned DrawableBrush did not render like the equivalent solid brush");
        });
    }

    private static Brush MakeSolid()
    {
        var brush = new SolidColorBrush();
        brush.Color.Value = Colors.Red;
        return brush;
    }

    private static Brush MakeDrawableBrush()
    {
        var content = new RectShape();
        content.AlignmentX.Value = AlignmentX.Center;
        content.AlignmentY.Value = AlignmentY.Center;
        content.Width.Value = 200;
        content.Height.Value = 200;
        content.Fill.Value = MakeSolid();
        var brush = new DrawableBrush();
        brush.Drawable.Value = content;
        brush.Stretch.Value = Stretch.Fill;
        // A stroke paints outside the brush frame, where TileMode.None decals to transparent; tiling keeps the
        // comparison against a solid brush apples-to-apples.
        brush.TileMode.Value = TileMode.Tile;
        return brush;
    }

    private static Drawable.Resource Make(Func<FilterEffect> makeEffect)
    {
        var shape = new RectShape();
        shape.AlignmentX.Value = AlignmentX.Center;
        shape.AlignmentY.Value = AlignmentY.Center;
        shape.Width.Value = 140;
        shape.Height.Value = 90;
        shape.Fill.Value = Brushes.White;
        shape.FilterEffect.Value = makeEffect();
        return shape.ToResource(CompositionContext.Default);
    }
}

// RegisterBrush states no ordering requirement: a typed operation authored between the registration and the
// operation that paints with the handle is lowered as its own fragment, and the handle must still reach the
// effect-item segment behind it.
internal sealed partial class TypedOperationBeforeBrushEffect : FilterEffect
{
    private const string IdentityShader = "half4 apply(half4 color) { return color; }";

    public TypedOperationBeforeBrushEffect()
    {
        ScanProperties<TypedOperationBeforeBrushEffect>();
    }

    public IExpressionProperty<Brush?> Brush { get; } = Property.CreateExpression<Brush?>();

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.Shader(ShaderDescription.CurrentPixel(IdentityShader));
            context.CustomEffect(
                new BrushPaintState(Brush),
                BrushPaintOperation.PaintBrush,
                static (_, bounds) => bounds);
        }
    }
}

// A registered brush stays resolvable while a custom effect paints its targets.
internal sealed partial class NestedActivateBrushEffect : FilterEffect
{
    public NestedActivateBrushEffect()
    {
        ScanProperties<NestedActivateBrushEffect>();
    }

    public IExpressionProperty<Brush?> Brush { get; } = Property.CreateExpression<Brush?>();

    public new partial class Resource
    {
        protected override void ApplyTo(FilterEffectContext context)
        {
            context.CustomEffect(
                new BrushPaintState(Brush),
                BrushPaintOperation.PaintBrush,
                static (_, bounds) => bounds);
        }
    }
}

internal sealed record BrushPaintState(Brush.Resource? Brush);

internal static class BrushPaintOperation
{
    public static void PaintBrush(BrushPaintState state, CustomFilterEffectContext context)
    {
        for (int i = 0; i < context.Targets.Count; i++)
        {
            EffectTarget target = context.Targets[i];
            if (target.RenderTarget is null)
                continue;

            Size size = target.Bounds.Size;
            EffectTarget newTarget = context.CreateTarget(target.Bounds);
            using var paint = new SKPaint();
            context.CreateBrushConstructor(new Rect(size), state.Brush, BlendMode.SrcIn, newTarget.Scale.Value)
                .ConfigurePaint(paint);
            using (ImmediateCanvas canvas = context.Open(newTarget))
            {
                canvas.Clear();
                using (canvas.PushDeviceSpace())
                {
                    canvas.DrawRenderTarget(target.RenderTarget, default);
                }

                canvas.Canvas.DrawRect(SKRect.Create(size.ToSKSize()), paint);
            }

            target.Dispose();
            context.Targets[i] = newTarget;
        }
    }
}
