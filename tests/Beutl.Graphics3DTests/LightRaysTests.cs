using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class LightRaysTests
{
    [TestCase(0.5f, 6f, 16f)]
    [TestCase(-1f, 10f, 24f)]
    public void BoundsIncludeTheOriginalAndOutwardRays(float center, float x, float width)
    {
        var effect = new LightRays { Center = { Value = new(center, 0.5f, RelativeUnit.Relative) }, Length = { Value = 100 } };
        ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
        Assert.That(graph.Passes.Select(pass => pass.Name), Is.EqualTo(new[] { "extract", "rays", "composite" }));
        Rect source = new(10, 20, 8, 6);
        Assert.That(graph.TransformBounds(source), Is.EqualTo(new Rect(x, 17, width, 12)));
        effect.Center.Value = new(center * source.Width, source.Height / 2, RelativeUnit.Absolute);
        Assert.That(ShaderGraphEffectTests.Record(effect).TransformBounds(source), Is.EqualTo(graph.TransformBounds(source)));
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void UnselectedPixelsKeepTheirOriginalColorAndOutline(float scale)
    {
        OnDevice(() =>
        {
            var effect = new LightRays
            {
                Center = { Value = new(-1, 0.5f, RelativeUnit.Relative) },
                Length = { Value = 100 },
                Threshold = { Value = 2 },
                SoftKnee = { Value = 0 },
            };
            Rect source = new(4, 6, 8, 8);
            var color = new Vector4(0.25f, 0.125f, 0.0625f, 0.5f);
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
            float[] pixels = ShaderGraphContractTests.Execute(graph, source, scale, sourceColor: color).Pixels;
            PixelRect output = PixelRect.FromRect(graph.TransformBounds(source), scale);
            PixelRect original = PixelRect.FromRect(source, scale);
            for (int y = output.Y; y < output.Bottom; y++)
                for (int x = output.X; x < output.Right; x++)
                {
                    Vector4 expected = x >= original.X && x < original.Right && y >= original.Y && y < original.Bottom
                        ? color : Vector4.Zero;
                    int index = ((y - output.Y) * output.Width + x - output.X) * 4;
                    Assert.That(pixels.AsSpan(index, 4).ToArray(),
                        Is.EqualTo(new[] { expected.X, expected.Y, expected.Z, expected.W }).Within(0.001));
                }
        });
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void AThinHighlightRemainsContinuousWithADistantExternalCenter(float scale)
    {
        OnDevice(() =>
        {
            var effect = new LightRays
            {
                Center = { Value = new(-256, 0.5f, RelativeUnit.Relative) },
                Length = { Value = 300 },
                Decay = { Value = 0 },
                Threshold = { Value = 0 },
            };
            Rect source = new(3, -5, 2, 2);
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
            PixelRect output = PixelRect.FromRect(graph.TransformBounds(source), scale);
            float[] pixels = ShaderGraphContractTests.Execute(graph, source, scale, sourceColor: Vector4.One).Pixels;
            int y = (int)MathF.Floor((source.Y + source.Height / 2) * scale) - output.Y;
            for (int x = (int)((source.Right + 2) * scale); x < output.Right - 3 * scale; x++)
                Assert.That(pixels[(y * output.Width + x - output.X) * 4 + 3], Is.GreaterThan(0), $"Gap at device x={x}.");
        });
    }

    [TestCase(0f, 1f)]
    [TestCase(50f, 1f)]
    [TestCase(100f, 1f)]
    [TestCase(50f, 2f)]
    public void DecayAndIntensityControlOnlyTheAddedPremultipliedLight(float decay, float intensity)
    {
        OnDevice(() =>
        {
            var effect = new LightRays
            {
                Length = { Value = 100 },
                Threshold = { Value = 0 },
                Decay = { Value = decay },
                Intensity = { Value = intensity },
            };
            Rect source = new(0, 0, 9, 9);
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
            PixelRect output = PixelRect.FromRect(graph.TransformBounds(source), 1);
            float[] pixels = ShaderGraphContractTests.Execute(graph, source, sourceColor: new(1, 0.5f, 0.25f, 0.5f)).Pixels;
            int center = ((4 - output.Y) * output.Width + 4 - output.X) * 4;
            float light = intensity / (decay / 25 + 1); // Integral of the falloff on a constant-color ray.
            Assert.That(pixels.AsSpan(center, 4).ToArray(), Is.EqualTo(new[]
            {
                1 + light, (1 + light) * 0.5f, (1 + light) * 0.25f, 0.5f + Math.Min(light * 0.5f, 1) * 0.5f,
            }).Within(0.003));
        });
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void PartialRenderingOfTheTrailRetainsItsOffscreenSource(float scale)
    {
        OnDevice(() =>
        {
            var effect = new LightRays { Center = { Value = new(-0.5f, 0.25f, RelativeUnit.Relative) }, Length = { Value = 100 } };
            Rect source = new(4, 6, 16, 12);
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
            Rect required = new(source.Right, source.Top, 8, source.Height);
            PixelRect output = PixelRect.FromRect(graph.TransformBounds(source), scale);
            PixelRect region = PixelRect.FromRect(required, scale);
            float[] full = ShaderGraphContractTests.Execute(graph, source, scale).Pixels;
            float[] partial = ShaderGraphContractTests.Execute(graph, source, scale, required).Pixels;
            float maximumAlpha = 0;
            for (int y = region.Y; y < region.Bottom; y++)
                for (int x = region.X; x < region.Right; x++)
                {
                    int index = ((y - output.Y) * output.Width + x - output.X) * 4;
                    for (int c = 0; c < 4; c++)
                        Assert.That(partial[index + c], Is.EqualTo(full[index + c]).Within(0.001));
                    maximumAlpha = Math.Max(maximumAlpha, partial[index + 3]);
                }
            Assert.That(maximumAlpha, Is.GreaterThan(0.01f));
        });
    }

    [Test]
    public void MultipleChildrenUseOneCenterForTheirBoundsAndSampling()
    {
        OnDevice(() =>
        {
            var effect = new LightRays { Length = { Value = 100 }, Decay = { Value = 0 }, Threshold = { Value = 0 } };
            using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
            using var context = new FilterEffectContext(new(0, 0, 100, 10));
            effect.ApplyTo(context, resource);
            using var brush = (Brush.Resource)new SolidColorBrush(Colors.White).ToResource(CompositionContext.Default);
            using var root = new FilterEffectRenderNode(resource);
            root.AddChild(new RectangleRenderNode(new(0, 0, 10, 10), brush, null));
            root.AddChild(new RectangleRenderNode(new(90, 0, 10, 10), brush, null));
            using var renderer = new RenderNodeRenderer(root,
                new RenderNodeRenderRequest { Intent = RenderIntent.Delivery, CacheOptions = RenderCacheOptions.Disabled });
            using RenderNodeRasterization result = renderer.Rasterize();
            PixelRect bounds = PixelRect.FromRect(context.Bounds, 1);
            Assert.That(PixelRect.FromRect(result.Bounds, 1), Is.EqualTo(bounds));
            foreach (int x in new[] { -18, 117 })
            {
                int index = ((5 - bounds.Y) * bounds.Width + x - bounds.X) * 4;
                Assert.That((float)result.Bitmap!.GetPixelSpan<Half>()[index + 3], Is.GreaterThan(0.02f));
            }
        });
    }

    [TestCase(0f, 1f)]
    [TestCase(50f, 0f)]
    public void ZeroLengthOrIntensityLeavesTheInputAlone(float length, float intensity)
    {
        var effect = new LightRays { Length = { Value = length }, Intensity = { Value = intensity } };
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(new(0, 0, 8, 8));
        effect.ApplyTo(context, resource);
        Assert.That(context.GetOrderedItems(), Is.Empty);
        Assert.That(context.Bounds, Is.EqualTo(new Rect(0, 0, 8, 8)));
    }

    [Test]
    public void SettingsRoundTripAndRecordedParametersAreSnapshots()
    {
        var effect = new LightRays
        {
            Center = { Value = new(-1, 0.25f, RelativeUnit.Relative) },
            Length = { Value = 125 },
            Decay = { Value = 70 },
            Intensity = { Value = 2 },
            Threshold = { Value = 0.75f },
            SoftKnee = { Value = 65 },
        };
        ShaderGraphDescription before = ShaderGraphEffectTests.Record(effect);
        var saved = CoreSerializer.SerializeToJsonObject(effect);
        var loaded = (LightRays)CoreSerializer.DeserializeFromJsonObject(saved, typeof(LightRays));
        Assert.That(ShaderGraphEffectTests.Record(loaded).OutputIdentity, Is.EqualTo(before.OutputIdentity));
        effect.Decay.Value = 20;
        ShaderGraphDescription after = ShaderGraphEffectTests.Record(effect);
        Assert.That(after.OutputIdentity, Is.Not.EqualTo(before.OutputIdentity));
        Assert.That(after.StructuralIdentity, Is.EqualTo(before.StructuralIdentity));
        Assert.That(ShaderGraphEffectTests.Record(loaded).OutputIdentity, Is.EqualTo(before.OutputIdentity));
    }

    private static void OnDevice(Action action)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(action);
    }
}
