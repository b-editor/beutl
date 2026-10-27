using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphReviewTests
{
    [TestCase(16f, 1f)]
    [TestCase(16.01f, 1f)]
    [TestCase(64f, 0.5f)]
    [TestCase(64f, 1f)]
    [TestCase(64f, 2f)]
    [TestCase(512f, 1f)]
    public void LargeBloomSpreadsASinglePixelWithoutGaps(float radius, float scale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new Bloom { Radius = { Value = radius }, Threshold = { Value = 0 } };
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
            Rect source = new(3, -5, 1, 1);
            PixelRect output = PixelRect.FromRect(graph.TransformBounds(source), scale);
            float[] pixels = ShaderGraphContractTests.Execute(graph, source, scale, sourceColor: Vector4.One).Pixels;
            int y = (int)MathF.Floor(source.Y * scale) - output.Y;
            int originX = (int)MathF.Floor(source.X * scale) - output.X;
            float previous = 1;
            for (int x = (int)(2 * scale); x < (int)(radius / 2 * scale); x++)
            {
                float alpha = pixels[(y * output.Width + x + originX) * 4 + 3];
                Assert.That(alpha, Is.GreaterThan(0), $"The glow has a gap at device x={x}.");
                Assert.That(alpha, Is.LessThanOrEqualTo(previous + 0.000001f), $"The glow repeats a highlight at x={x}.");
                previous = alpha;
            }
        });
    }

    [TestCase(500f, 1920, 1080, 1f)]
    [TestCase(500f, 1920, 1080, 2f)]
    [TestCase(512f, 64, 64, 1f)]
    [TestCase(512f, 64, 64, 2f)]
    public void LargeBloomKeepsBlurTargetsSmall(float radius, int width, int height, float scale)
    {
        ShaderGraphDescription graph = ShaderGraphEffectTests.Record(new Bloom { Radius = { Value = radius } });
        Rect source = new(0, 0, width, height);
        var frame = new ShaderImageFrame(source, source, PixelRect.FromRect(source, scale).Size, scale);
        var regions = new ShaderGraphRegions(graph, frame, scale, default, graph.TransformBounds(source));
        foreach (ShaderPassDescription pass in graph.Passes.Where(p => p.Name.StartsWith("blur-")))
        {
            ShaderImageFrame target = regions.Frames[pass.Attachments.Single().Image.Id];
            Assert.That(radius * target.Scale, Is.LessThanOrEqualTo(16 * scale));
            Assert.That((long)target.PixelSize.Width * target.PixelSize.Height, Is.LessThan(25_000));
        }
    }

    [TestCase(false, 0.5f)]
    [TestCase(false, 2f)]
    [TestCase(true, 1f)]
    public void DownsampledBloomPreservesPartialRenderingAndAlpha(bool preserveAlpha, float scale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new Bloom { Radius = { Value = 64 }, Threshold = { Value = 0 }, PreserveAlpha = { Value = preserveAlpha } };
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
            Rect source = new(3, -5, 9, 7);
            PixelRect output = PixelRect.FromRect(graph.TransformBounds(source), scale);
            Rect required = source.Inflate(preserveAlpha ? 0 : 4);
            var color = new Vector4(0.5f);
            float[] full = ShaderGraphContractTests.Execute(graph, source, scale, sourceColor: color).Pixels;
            float[] partial = ShaderGraphContractTests.Execute(graph, source, scale, required, color).Pixels;
            PixelRect selected = PixelRect.FromRect(required, scale);
            for (int y = selected.Y; y < selected.Bottom; y++)
                for (int x = selected.X; x < selected.Right; x++)
                {
                    int index = ((y - output.Y) * output.Width + x - output.X) * 4;
                    for (int c = 0; c < 4; c++)
                        Assert.That(partial[index + c], Is.EqualTo(full[index + c]).Within(0.001));
                    if (preserveAlpha)
                    {
                        Assert.That(full[index + 3], Is.EqualTo(0.5f).Within(0.001));
                        Assert.That(full[index], Is.GreaterThan(0.5f));
                    }
                }
        });
    }

    [TestCase("unfinished shader")]
    [TestCase("#version 450\nlayout(binding=3) uniform sampler2D missing; layout(location=0) out vec4 color; void main(){color=texture(missing,vec2(0.5));}")]
    [TestCase("#version 450\nlayout(location=0) in vec3 wrongInput; layout(location=0) out vec4 color; void main(){color=vec4(wrongInput,1);}")]
    public void InvalidScriptIsInactiveAndARepairedScriptRenders(string invalid)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            const string valid = "#version 450\nlayout(location=0) out vec4 color; void main(){color=vec4(1,0,0,1);}";
            var effect = new GLSLScriptEffect();
            using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
            using var brush = (Brush.Resource)new SolidColorBrush(Colors.White).ToResource(CompositionContext.Default);
            using var root = new FilterEffectRenderNode(resource);
            root.AddChild(new RectangleRenderNode(new(0, 0, 8, 8), brush, null));
            using var renderer = new RenderNodeRenderer(root,
                new RenderNodeRenderRequest { Intent = RenderIntent.Delivery, CacheOptions = RenderCacheOptions.Disabled });
            foreach (string text in new[] { invalid, invalid, valid, invalid, valid })
            {
                effect.FragmentShader.Value = text;
                bool changed = false;
                resource.Reconcile(effect, CompositionContext.Default, ref changed);
                root.Update(resource);
                using RenderNodeRasterization result = renderer.Rasterize();
                float[] pixel = result.Bitmap!.GetPixelSpan<Half>()[..4].ToArray().Select(v => (float)v).ToArray();
                Assert.That(pixel, Is.EqualTo(text == valid ? new[] { 1f, 0, 0, 1 } : new[] { 1f, 1, 1, 1 }));
            }
        });
    }

    [Test]
    public void PendingScriptCompilationInvalidatesRecordedPassThrough()
    {
        var effect = new GLSLScriptEffect { FragmentShader = { Value = "" } };
        using var resource = (GLSLScriptEffect.Resource)effect.ToResource(CompositionContext.Default);
        // Reconciliation has copied the text, but compiler initialization has not succeeded yet.
        effect.FragmentShader.Value = resource.FragmentShader = "#version 450\nlayout(location=0) out vec4 color; void main(){color=vec4(1);}";
        using var node = new FilterEffectRenderNode(resource);
        bool changed = false;
        resource.Reconcile(effect, CompositionContext.Default, ref changed);
        ShaderProgram? program = resource._program;
        Assert.That(program, Is.Not.Null);
        Assert.That(node.Update(resource), Is.True);

        resource.Reconcile(effect, CompositionContext.Default, ref changed);
        Assert.That(resource._program, Is.SameAs(program));
        Assert.That(node.Update(resource), Is.False);
    }

    [TestCase(RadialBlurMode.Zoom)]
    [TestCase(RadialBlurMode.Spin)]
    public void RadialBlurSharesItsCenterAcrossValueProducingChildren(RadialBlurMode mode)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new RadialBlur { Mode = { Value = mode }, Amount = { Value = 90 }, Samples = { Value = 65 } };
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
            PixelRect expected = PixelRect.FromRect(context.Bounds, 1);
            Assert.That(PixelRect.FromRect(result.Bounds, 1), Is.EqualTo(expected));
            for (int i = 0; i < 2; i++)
            {
                float offset = 5 + i * 90 - 50;
                Point trail = mode == RadialBlurMode.Zoom ? new(50 + offset * 1.5f, 5)
                    : new(50 + offset * MathF.Cos(MathF.PI / 6), 5 - offset * MathF.Sin(MathF.PI / 6));
                int x = (int)MathF.Floor(trail.X) - expected.X, y = (int)MathF.Floor(trail.Y) - expected.Y;
                float alpha = (float)result.Bitmap!.GetPixelSpan<Half>()[(y * result.Bitmap.Width + x) * 4 + 3];
                Assert.That(alpha, Is.GreaterThan(0.02f), $"Child {i} must leave a trail around the shared center.");
            }
        });
    }
}
