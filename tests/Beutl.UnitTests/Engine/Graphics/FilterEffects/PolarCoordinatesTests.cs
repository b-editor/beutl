using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class PolarCoordinatesTests
{
    private const int Width = 129;
    private const int Height = 81;
    private static readonly PixelSize s_size = new(Width, Height);
    private static readonly Rect s_bounds = new(10.25f, -20.5f, 100, 60);

    [TestCase(PolarCoordinatesDirection.RectangularToPolar, 0f)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular, 0f)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, -20f)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, float.NaN)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, float.PositiveInfinity)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, float.NegativeInfinity)]
    public void ZeroOrInvalidAmountLeavesFinitePixelsUnchanged(PolarCoordinatesDirection direction, float amount)
    {
        // Set the resource directly to exercise values that can bypass property validation.
        using var resource = new PolarCoordinates.Resource { Direction = direction, Amount = amount };
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawGradient);

        Assert.Multiple(() =>
        {
            Assert.That(after.All(float.IsFinite), Is.True);
            Assert.That(after, Is.EqualTo(before));
        });
    }

    [Test]
    public void RectangularToPolarWrapsRowsIntoAnInscribedEllipse()
    {
        using PolarCoordinates.Resource resource = new PolarCoordinates().ToResource(CompositionContext.Default);
        (_, float[] pixels) = FilterEffectTestHarness.RunOnTarget(resource, s_size, canvas =>
        {
            canvas.Clear(SKColors.Lime);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(new SKRect(0, 0, Width, 6), paint);
            paint.Color = SKColors.Blue;
            canvas.DrawRect(new SKRect(0, Height - 6, Width, Height), paint);
        });

        Assert.Multiple(() =>
        {
            Assert.That(Channel(pixels, Width / 2, Height / 2, 0), Is.GreaterThan(0.99f), "top row at the center");
            foreach ((int x, int y) in new[] { (Width / 2, 0), (0, Height / 2), (Width - 1, Height / 2), (Width / 2, Height - 1) })
                Assert.That(Channel(pixels, x, y, 2), Is.GreaterThan(0.99f), $"bottom row at the rim ({x}, {y})");
            foreach ((int x, int y) in new[] { (0, 0), (Width - 1, 0), (0, Height - 1), (Width - 1, Height - 1) })
                Assert.That(Channel(pixels, x, y, 3), Is.LessThan(0.001f), $"transparent corner ({x}, {y})");
        });
    }

    [Test]
    public void AmountBlendsTheSamplingCoordinates()
    {
        var effect = new PolarCoordinates { Amount = { Value = 50f } };
        using PolarCoordinates.Resource resource = effect.ToResource(CompositionContext.Default);
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawGradient);

        // The center's mapped sample is the first texel, so 50% samples halfway to it.
        float expected = Channel(before, Width / 4, Height / 4, 0);
        Assert.That(Channel(after, Width / 2, Height / 2, 0), Is.EqualTo(expected).Within(0.005f));
    }

    [Test]
    public void PolarToRectangularRecoversTheInterior()
    {
        var group = new FilterEffectGroup
        {
            Children =
            {
                new PolarCoordinates(),
                new PolarCoordinates { Direction = { Value = PolarCoordinatesDirection.PolarToRectangular } },
            },
        };
        using FilterEffect.Resource resource = group.ToResource(CompositionContext.Default);
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnTarget(resource, s_size, DrawGradient);

        // Near the collapsed center and the seam, multiple source pixels can share an output texel.
        float maximumError = 0f;
        for (int y = Height / 4; y < Height - 10; y++)
        {
            for (int x = 12; x < Width - 12; x++)
            {
                for (int channel = 0; channel < 4; channel++)
                    maximumError = MathF.Max(maximumError, MathF.Abs(Channel(after, x, y, channel) - Channel(before, x, y, channel)));
            }
        }

        Assert.That(maximumError, Is.LessThan(0.04f));
    }

    [TestCase(PolarCoordinatesDirection.RectangularToPolar)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular)]
    public void KeepsInputBoundsAndRequiresTheFullSource(PolarCoordinatesDirection direction)
    {
        var effect = new PolarCoordinates { Direction = { Value = direction } };
        (ShaderDescription description, Rect output) = FilterEffectTestHarness.RecordShader(effect, s_bounds);

        Assert.Multiple(() =>
        {
            Assert.That(output, Is.EqualTo(s_bounds));
            Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(description.Bounds.RequiresFullInput, Is.True);
            Assert.That(description.SourceTileMode, Is.EqualTo(SKShaderTileMode.Decal));
        });
    }

    [Test]
    public void TwelveOClockSeamIsOpaque()
    {
        (_, float[] pixels) = FilterEffectTestHarness.RunOnSolidTarget(new PolarCoordinates(), SKColors.White, s_size);

        for (int y = 4; y < Height / 2 - 4; y++)
        {
            for (int x = Width / 2 - 1; x <= Width / 2 + 1; x++)
                Assert.That(Channel(pixels, x, y, 3), Is.GreaterThan(0.99f), $"seam pixel ({x}, {y})");
        }
    }

    [TestCase(PolarCoordinatesDirection.RectangularToPolar, 100f, 75f, 25f)]
    [TestCase(PolarCoordinatesDirection.RectangularToPolar, 50f, 75f, 50f)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular, 100f, 25f, 75f)]
    [TestCase(PolarCoordinatesDirection.PolarToRectangular, 50f, 25f, 50f)]
    public void HitTestUsesTheMappedSourcePoint(PolarCoordinatesDirection direction, float amount, float outputX, float sourceX)
    {
        var effect = new PolarCoordinates { Direction = { Value = direction }, Amount = { Value = amount } };
        (ShaderDescription description, Rect output) = FilterEffectTestHarness.RecordShader(effect, s_bounds);
        RenderHitTestContract hitTest = description.HitTest!.Value;
        var accepted = new Rect(s_bounds.X + sourceX - 1, s_bounds.Center.Y - 1, 2, 2);
        var input = new RenderHitTestInput(s_bounds, accepted.ContainsExclusive);
        var point = new Point(s_bounds.X + outputX, s_bounds.Center.Y);

        Assert.Multiple(() =>
        {
            Assert.That(hitTest.Evaluate(output, [input], [], point), Is.True);
            Assert.That(hitTest.Evaluate(output, [input], [], point + new Vector(0, 10)), Is.False);
            Assert.That(hitTest.Evaluate(output, [new RenderHitTestInput(s_bounds, _ => true)], [], s_bounds.BottomRight), Is.False);
        });
    }

    private static void DrawGradient(SKCanvas canvas)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(Width, Height), [SKColors.Black, SKColors.White], [0f, 1f], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { Shader = shader };
        canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
    }

    private static float Channel(float[] pixels, int x, int y, int channel) => pixels[((y * Width + x) * 4) + channel];
}
