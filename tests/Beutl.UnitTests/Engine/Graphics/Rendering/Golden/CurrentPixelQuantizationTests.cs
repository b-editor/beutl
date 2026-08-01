using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public sealed class CurrentPixelQuantizationTests
{
    private static readonly PixelSize s_frame = new(32, 32);

    [TestCase(false)]
    [TestCase(true)]
    public void DoubleInvert_MatchesIdentityMaterialization(bool fusionEnabled)
    {
        FusionMode fusionMode = fusionEnabled ? FusionMode.Enabled : FusionMode.Disabled;
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using Bitmap control = Render(CreateDrawable(invertCount: 0), fusionMode);
            using Bitmap identity = Render(CreateDrawable(invertCount: -1), fusionMode);
            using Bitmap inverted = Render(CreateDrawable(invertCount: 2), fusionMode);

            byte controlRed = ReadRed(control);
            byte identityRed = ReadRed(identity);
            byte invertedRed = ReadRed(inverted);
            (float controlLinear, float controlAlpha) = ReadLinear(control);
            (float identityLinear, float identityAlpha) = ReadLinear(identity);
            (float invertedLinear, float invertedAlpha) = ReadLinear(inverted);
            TestContext.WriteLine(
                $"fusion={fusionEnabled}, control={controlRed} ({controlLinear:R}, a={controlAlpha:R}), "
                + $"identity-stage={identityRed} ({identityLinear:R}, a={identityAlpha:R}), "
                + $"double-invert={invertedRed} ({invertedLinear:R}, a={invertedAlpha:R})");
            Assert.Multiple(() =>
            {
                Assert.That(controlLinear, Is.GreaterThan(0), "The unfiltered control must contain visible color.");
                Assert.That(controlAlpha, Is.GreaterThan(0), "The unfiltered control must contain visible alpha.");
                Assert.That(identityRed, Is.EqualTo(controlRed).Within(2),
                    "Identity materialization must preserve the rendered control color "
                    + "within the RGBA16F round-trip quantization this fixture characterizes.");
                Assert.That(identityLinear, Is.EqualTo(controlLinear).Within(0.003f),
                    "Identity materialization must preserve the rendered control in linear space.");
                Assert.That(identityAlpha, Is.EqualTo(controlAlpha).Within(0.001f),
                    "Identity materialization must preserve the rendered control alpha.");
                Assert.That(invertedRed, Is.EqualTo(identityRed),
                    $"Two full Invert stages must not add quantization beyond the materialization boundary; identity={identityRed}, inverted={invertedRed}.");
                Assert.That(invertedLinear, Is.EqualTo(identityLinear).Within(0.001f),
                    "Two full Invert stages must preserve the identity materialization in linear space.");
                Assert.That(invertedAlpha, Is.EqualTo(identityAlpha).Within(0.001f),
                    "Two full Invert stages must preserve the identity materialization alpha.");
            });
        });
    }

    private static Drawable.Resource CreateDrawable(int invertCount, float amount = 100)
    {
        var shape = new RectShape
        {
            Width = { CurrentValue = s_frame.Width },
            Height = { CurrentValue = s_frame.Height },
            Fill = { CurrentValue = new SolidColorBrush(new Color(255, 51, 51, 51)) },
            AlignmentX = { CurrentValue = AlignmentX.Center },
            AlignmentY = { CurrentValue = AlignmentY.Center },
        };
        if (invertCount < 0)
        {
            shape.FilterEffect.CurrentValue = new IdentityTypedShaderEffect();
        }
        else if (invertCount > 0)
        {
            var group = new FilterEffectGroup();
            for (int index = 0; index < invertCount; index++)
            {
                group.Children.Add(new Invert
                {
                    Amount = { CurrentValue = amount },
                });
            }

            shape.FilterEffect.CurrentValue = group;
        }

        return shape.ToResource(CompositionContext.Default);
    }

    private static Bitmap Render(Drawable.Resource resource, FusionMode fusionMode)
    {
        using (resource)
        using (var node = new DrawableRenderNode(resource))
        {
            using (var graphics = new GraphicsContext2D(node, s_frame.ToSize(1), 1))
            {
                resource.GetOriginal().Render(graphics, resource);
            }

            using RenderTarget target = RenderTarget.Create(s_frame.Width, s_frame.Height)
                                        ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            using var canvas = new ImmediateCanvas(target, 1, logicalSize: s_frame.ToSize(1));
            canvas.Clear();
            using var renderer = new RenderNodeRenderer(
                node,
                new RenderNodeRendererOptions
                {
                    DefaultRequest = new RenderNodeRenderRequest
                    {
                        Intent = RenderIntent.Delivery,
                        TargetDomain = new Rect(default, s_frame.ToSize(1)),
                        OutputScale = 1,
                        CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                        FusionMode = fusionMode,
                    },
                });
            renderer.Render(canvas);
            return target.Snapshot();
        }
    }

    private static byte ReadRed(Bitmap bitmap)
    {
        (float red, _) = ReadLinear(bitmap);
        return Color.FromLinear(new Vector4(red, red, red, 1)).R;
    }

    private static (float Red, float Alpha) ReadLinear(Bitmap bitmap)
    {
        int offset = ((bitmap.Height / 2 * bitmap.Width) + bitmap.Width / 2) * 4;
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        return (
            (float)BitConverter.UInt16BitsToHalf(pixels[offset]),
            (float)BitConverter.UInt16BitsToHalf(pixels[offset + 3]));
    }
}
