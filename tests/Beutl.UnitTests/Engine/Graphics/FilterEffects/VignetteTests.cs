using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.FilterEffects;

[TestFixture]
public sealed class VignetteTests
{
    private static readonly PixelSize s_square = new(65, 65);

    [Test]
    public void ZeroAmountPreservesRenderedPixels()
    {
        var effect = new Vignette { Amount = { Value = 0f } };
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnSolidTarget(effect, SKColors.Coral, s_square);

        Assert.That(after, Is.EqualTo(before));
    }

    [TestCase(0f)]
    [TestCase(float.NaN)]
    [TestCase(float.NegativeInfinity)]
    [TestCase(float.PositiveInfinity)]
    public void ZeroOrNonFiniteAmountRecordsNothing(float amount)
    {
        using var resource = new Vignette.Resource { Amount = amount };
        using var context = new FilterEffectContext(new Rect(0, 0, s_square.Width, s_square.Height));
        context.ApplyTransactional(resource);

        Assert.That(context.GetOrderedItems(), Is.Empty);
    }

    [Test]
    public void PositiveAmountDarkensTowardTheCornerAndPreservesTheCenter()
    {
        float[] pixels = RenderWhite(new Vignette(), s_square);
        float center = ChannelAt(pixels, s_square, 32, 32);
        float inner = ChannelAt(pixels, s_square, 22, 22);
        float middle = ChannelAt(pixels, s_square, 16, 16);
        float outer = ChannelAt(pixels, s_square, 10, 10);
        float corner = ChannelAt(pixels, s_square, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(center, Is.EqualTo(1f));
            Assert.That(inner, Is.LessThan(center));
            Assert.That(middle, Is.LessThan(inner));
            Assert.That(outer, Is.LessThan(middle));
            Assert.That(corner, Is.LessThan(outer).And.EqualTo(0.5f));
        });
    }

    [Test]
    public void NegativeAmountLightensTowardPremultipliedWhite()
    {
        var effect = new Vignette { Amount = { Value = -100f } };
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnSolidTarget(
            effect, new SKColor(40, 100, 150, 96), s_square);

        Assert.Multiple(() =>
        {
            for (int channel = 0; channel < 3; channel++)
            {
                float center = ChannelAt(after, s_square, 32, 32, channel);
                Assert.That(center, Is.EqualTo(ChannelAt(before, s_square, 32, 32, channel)));
                Assert.That(ChannelAt(after, s_square, 16, 16, channel),
                    Is.GreaterThan(center).And.LessThan(before[3]));
                Assert.That(after[channel], Is.EqualTo(before[3]).And.GreaterThan(before[channel]));
            }
            Assert.That(Enumerable.Range(0, after.Length / 4).All(pixel =>
            {
                int i = pixel * 4;
                return after[i] <= after[i + 3] && after[i + 1] <= after[i + 3] && after[i + 2] <= after[i + 3];
            }), Is.True);
        });
    }

    [TestCase(100f)]
    [TestCase(-100f)]
    public void AlphaAndHitTestingPassThroughToTheInput(float amount)
    {
        var effect = new Vignette { Amount = { Value = amount } };
        (float[] before, float[] after) = FilterEffectTestHarness.RunOnSolidTarget(
            effect, new SKColor(230, 40, 20, 160), s_square);
        (_, float[] transparent) = FilterEffectTestHarness.RunOnSolidTarget(effect, SKColors.Transparent, s_square);
        var bounds = new Rect(10.25f, 20.5f, 65, 65);
        (ShaderDescription description, Rect output) = FilterEffectTestHarness.RecordShader(effect, bounds);
        var input = new RenderHitTestInput(bounds, point => bounds.ContainsExclusive(point) && point.X >= bounds.Center.X);
        bool Hit(float x, float y) => description.HitTest!.Value.Evaluate(
            output, [input], [], new Point(bounds.X + x, bounds.Y + y));

        Assert.Multiple(() =>
        {
            Assert.That(after.Where((_, index) => index % 4 == 3),
                Is.EqualTo(before.Where((_, index) => index % 4 == 3)));
            Assert.That(transparent, Is.All.Zero);
            Assert.That(output, Is.EqualTo(bounds));
            Assert.That(description.Kind, Is.EqualTo(ShaderDescriptionKind.WholeSource));
            Assert.That(description.SourceTileMode, Is.EqualTo(SKShaderTileMode.Decal));
            Assert.That(Hit(64, 0), Is.True, "the corner keeps the input's hit even at maximum strength");
            Assert.That(Hit(16, 32), Is.False, "the source's transparent region is still ignored");
            Assert.That(Hit(80, 32), Is.False, "points outside the input are still ignored");
        });
    }

    [TestCase(8.5f / 65f, 0.5f, RelativeUnit.Relative)]
    [TestCase(8.5f, 32.5f, RelativeUnit.Absolute)]
    public void MovingCenterMovesTheBrightestRegion(float x, float y, RelativeUnit unit)
    {
        var effect = new Vignette { Amount = { Value = 100f } };
        float[] centered = RenderWhite(effect, s_square);
        effect.Center.Value = new RelativePoint(x, y, unit);
        float[] moved = RenderWhite(effect, s_square);

        Assert.Multiple(() =>
        {
            Assert.That(ChannelAt(centered, s_square, 32, 32), Is.EqualTo(1f));
            Assert.That(ChannelAt(centered, s_square, 8, 32), Is.LessThan(1f));
            Assert.That(ChannelAt(moved, s_square, 8, 32), Is.EqualTo(1f));
            Assert.That(ChannelAt(moved, s_square, 32, 32), Is.LessThan(1f));
        });
    }

    [Test]
    public void MaximumRoundnessIsCircularOnNonSquareBounds()
    {
        var size = new PixelSize(129, 65);
        var effect = new Vignette { Amount = { Value = 100f }, Roundness = { Value = 100f } };
        float[] circle = RenderWhite(effect, size);
        effect.Roundness.Value = 0f;
        float[] ellipse = RenderWhite(effect, size);

        Assert.Multiple(() =>
        {
            float horizontal = ChannelAt(circle, size, 94, 32);
            Assert.That(horizontal, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(ChannelAt(circle, size, 64, 62), Is.EqualTo(horizontal).Within(0.001f));
            Assert.That(ChannelAt(ellipse, size, 64, 62), Is.LessThan(ChannelAt(ellipse, size, 94, 32)));
        });
    }

    [Test]
    public void NegativeRoundnessFlattensTheSidesWithoutChangingTheCornerRadius()
    {
        var effect = new Vignette { Amount = { Value = 100f }, Feather = { Value = 100f } };
        float[] ellipse = RenderWhite(effect, s_square);
        effect.Roundness.Value = -100f;
        float[] rounded = RenderWhite(effect, s_square);

        Assert.Multiple(() =>
        {
            Assert.That(ChannelAt(rounded, s_square, 32, 12), Is.LessThan(ChannelAt(ellipse, s_square, 32, 12)));
            Assert.That(ChannelAt(rounded, s_square, 12, 12),
                Is.EqualTo(ChannelAt(ellipse, s_square, 12, 12)).Within(0.001f));
        });
    }

    [TestCase(10f)]
    [TestCase(100f)]
    public void MidpointStaysAtHalfStrengthAsFeatherChanges(float feather)
    {
        var effect = new Vignette { Amount = { Value = 100f }, Midpoint = { Value = 40f }, Feather = { Value = feather } };
        float[] pixels = RenderWhite(effect, s_square);
        // This sample is 13 pixels from the center on both axes, or 40% of the way to the corner.
        Assert.That(ChannelAt(pixels, s_square, 19, 19), Is.EqualTo(0.5f).Within(0.002f));
    }

    [Test]
    public void ZeroFeatherMakesAFiniteHardStep()
    {
        var size = new PixelSize(96, 128);
        var effect = new Vignette
        {
            Amount = { Value = 100f },
            Roundness = { Value = 100f },
            Feather = { Value = 0f },
            Center = { Value = new RelativePoint(48.5f, 64.5f, RelativeUnit.Absolute) },
        };
        float[] pixels = RenderWhite(effect, size);

        Assert.Multiple(() =>
        {
            Assert.That(pixels.All(float.IsFinite), Is.True);
            // The circular radius is 80 pixels, so the pixel 40 pixels from the center is exactly at the step.
            Assert.That(ChannelAt(pixels, size, 87, 64), Is.EqualTo(1f));
            Assert.That(ChannelAt(pixels, size, 88, 64), Is.EqualTo(0.5f));
            Assert.That(ChannelAt(pixels, size, 89, 64), Is.Zero);
        });
    }

    [Test]
    public void DetachedValuesUseRangesAndFiniteDefaults()
    {
        using var resource = new Vignette.Resource
        {
            Amount = 300f,
            Midpoint = float.NaN,
            Roundness = -300f,
            Feather = 200f,
            Center = new RelativePoint(float.NaN, 0.1f, RelativeUnit.Relative),
        };
        var expected = new Vignette
        {
            Amount = { Value = 100f },
            Roundness = { Value = -100f },
            Feather = { Value = 100f },
        };
        var bounds = new Rect(0, 0, s_square.Width, s_square.Height);
        using var context = new FilterEffectContext(bounds);
        context.ApplyTransactional(resource);
        ShaderDescription actual = ((FEItem_Shader)context.GetOrderedItems().Single()).Description;
        ShaderDescription clamped = FilterEffectTestHarness.RecordShader(expected, bounds).Description;

        Assert.Multiple(() =>
        {
            foreach (ShaderUniformBinding uniform in actual.Uniforms)
            {
                Assert.That(FilterEffectTestHarness.BindUniform(actual, uniform.Name, bounds).Floats,
                    Is.EqualTo(FilterEffectTestHarness.BindUniform(clamped, uniform.Name, bounds).Floats), uniform.Name);
            }
        });
    }

    [TestCase(0, 60f)]
    [TestCase(1, 0f)]
    [TestCase(2, -60f)]
    public void SerializationPreservesAmountAnimationAcrossZero(int seconds, float expectedAmount)
    {
        var effect = new Vignette
        {
            Amount = { Value = -20f },
            Midpoint = { Value = 35f },
            Roundness = { Value = -15f },
            Feather = { Value = 25f },
            Center = { Value = new RelativePoint(0.25f, 0.75f, RelativeUnit.Relative) },
        };
        effect.Amount.SetKeyFrameAnimation(new KeyFrameAnimation<float>
        {
            KeyFrames =
            {
                new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 60f } },
                new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = -60f } },
            },
        });
        var json = CoreSerializer.SerializeToJsonObject(effect);
        var restored = (Vignette)CoreSerializer.DeserializeFromJsonObject(json, typeof(Vignette));
        using Vignette.Resource resource = restored.ToResource(new CompositionContext(MediaTime.FromSeconds(seconds)));

        Assert.Multiple(() =>
        {
            Assert.That(restored.Amount.Value, Is.EqualTo(effect.Amount.Value));
            Assert.That(restored.Midpoint.Value, Is.EqualTo(effect.Midpoint.Value));
            Assert.That(restored.Roundness.Value, Is.EqualTo(effect.Roundness.Value));
            Assert.That(restored.Feather.Value, Is.EqualTo(effect.Feather.Value));
            Assert.That(restored.Center.Value, Is.EqualTo(effect.Center.Value));
            Assert.That(resource.Amount, Is.EqualTo(expectedAmount));
        });
    }

    [Test]
    public void SourceBackdropAppliesTheVignetteAcrossTheFrame()
    {
        var size = new PixelSize(101, 61);
        var scene = new DrawableGroup();
        scene.Children.Add(new RectShape
        {
            Width = { Value = 21 },
            Height = { Value = 21 },
            Fill = { Value = Brushes.Gray },
        });
        using Drawable.Resource input = scene.ToResource(CompositionContext.Default);
        using Bitmap before = GoldenImageHarness.RenderAtScale(input, size, 1f, clearColor: Colors.White);
        scene.Children.Add(new SourceBackdrop
        {
            FilterEffect = { Value = new Vignette { Amount = { Value = 100f } } },
        });
        using Drawable.Resource output = scene.ToResource(CompositionContext.Default);
        using Bitmap after = GoldenImageHarness.RenderAtScale(output, size, 1f, clearColor: Colors.White);
        ReadOnlySpan<ushort> source = before.GetPixelSpan<ushort>();
        ReadOnlySpan<ushort> pixels = after.GetPixelSpan<ushort>();

        using (Assert.EnterMultipleScope())
        {
            int center = ((size.Height / 2) * size.Width + size.Width / 2) * 4;
            Assert.That(pixels[center], Is.EqualTo(source[center]));
            foreach ((int x, int y) in new[] { (0, 0), (size.Width - 1, 0), (0, size.Height - 1), (size.Width - 1, size.Height - 1) })
            {
                int index = (y * size.Width + x) * 4;
                Assert.That((float)BitConverter.UInt16BitsToHalf(source[index]), Is.EqualTo(1f));
                Assert.That((float)BitConverter.UInt16BitsToHalf(pixels[index]), Is.Zero);
                Assert.That(pixels[index + 3], Is.EqualTo(source[index + 3]));
            }
        }
    }

    private static float[] RenderWhite(Vignette effect, PixelSize size)
        => FilterEffectTestHarness.RunOnSolidTarget(effect, SKColors.White, size).After;

    private static float ChannelAt(float[] pixels, PixelSize size, int x, int y, int channel = 0)
        => pixels[(y * size.Width + x) * 4 + channel];
}
