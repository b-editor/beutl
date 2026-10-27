using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphEffectTests
{
    [TestCase(false, 4)]
    [TestCase(true, 2)]
    public void EffectResourcesRecordOnlyDeclarativePasses(bool refraction, int passes)
    {
        FilterEffect effect = Create(refraction);
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(ShaderGraphRenderingTests.SourceBounds);
        context.ApplyTransactional(effect, resource);
        Assert.That(context.GetOrderedItems(), Has.Count.EqualTo(1));
        var item = (FEItem_ShaderGraph)context.GetOrderedItems().Single();
        Assert.That(item.Description.Passes, Has.Length.EqualTo(passes));
        Assert.That(item.Description.TransformBounds(ShaderGraphRenderingTests.SourceBounds),
            Is.EqualTo(ShaderGraphRenderingTests.SourceBounds.Inflate(4)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EffectSettingsRoundTripAndRecordedValuesAreSnapshots(bool refraction)
    {
        FilterEffect effect = Create(refraction);
        ShaderGraphDescription before = Record(effect);
        var group = new FilterEffectGroup { Children = { effect } };
        var saved = CoreSerializer.SerializeToJsonObject(group);
        var loaded = (FilterEffectGroup)CoreSerializer.DeserializeFromJsonObject(saved, typeof(FilterEffectGroup));
        Assert.That(Record(loaded.Children.Single()).OutputIdentity, Is.EqualTo(before.OutputIdentity));
        if (effect is Bloom bloom) bloom.Intensity.Value = 2;
        else ((WaveRefraction)effect).Phase.Value = 90;
        ShaderGraphDescription after = Record(effect);
        Assert.That(after.OutputIdentity, Is.Not.EqualTo(before.OutputIdentity));
        Assert.That(after.StructuralIdentity, Is.EqualTo(before.StructuralIdentity));
        Assert.That(Record(loaded.Children.Single()).OutputIdentity, Is.EqualTo(before.OutputIdentity));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BloomCompositesLinearHighlightsAndMasksWithOriginalAlpha(bool preserveAlpha)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bloom = new Bloom
            {
                Radius = { Value = 0 },
                Threshold = { Value = 0.25f },
                Intensity = { Value = 0.5f },
                PreserveAlpha = { Value = preserveAlpha },
            };
            float[] pixels = ShaderGraphContractTests.Execute(Record(bloom), new(0, 0, 8, 8),
                sourceColor: new Vector4(1, 1, 1, 0.5f)).Pixels;
            Assert.That(pixels.Take(4), Is.EqualTo(preserveAlpha
                ? new[] { 1.21875f, 1.21875f, 1.21875f, 0.5f }
                : new[] { 1.4375f, 1.4375f, 1.4375f, 0.609375f }).Within(0.003));
        });
    }

    [TestCase(false, 0.5f)]
    [TestCase(false, 1f)]
    [TestCase(false, 2f)]
    [TestCase(true, 0.5f)]
    [TestCase(true, 1f)]
    [TestCase(true, 2f)]
    public void PartialRenderingPreservesEffectsOutsideTheSource(bool refraction, float scale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            ShaderGraphDescription graph = Record(Create(refraction));
            Rect source = ShaderGraphRenderingTests.SourceBounds;
            Rect bounds = graph.TransformBounds(source);
            Rect region = new(source.X - 2, source.Y, 8, 6);
            float[] full = ShaderGraphRenderingTests.Render(graph, bounds, scale);
            float[] partial = ShaderGraphRenderingTests.Render(graph, bounds, scale, region);
            PixelRect selected = PixelRect.FromRect(region.Translate(-bounds.Position), scale);
            int width = PixelRect.FromRect(bounds, scale).Width;
            for (int y = selected.Y; y < selected.Bottom; y++)
                for (int x = selected.X; x < selected.Right; x++)
                    for (int channel = 0; channel < 4; channel++)
                    {
                        int index = (y * width + x) * 4 + channel;
                        Assert.That(partial[index], Is.EqualTo(full[index]).Within(0.003), $"({x}, {y}), {channel}");
                    }
            int outsideX = (int)((source.X - bounds.X) * scale) - 1;
            Assert.That(Enumerable.Range(0, PixelRect.FromRect(bounds, scale).Height)
                .Select(y => full[(y * width + outsideX) * 4 + 3]), Has.Some.GreaterThan(0.001f));
        });
    }

    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void RefractionUsesLogicalWaveCoordinatesAtEveryDensity(float scale)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new WaveRefraction { Amount = { Value = 2 }, Wavelength = { Value = 16 }, Phase = { Value = 45 } };
            Rect source = new(4, 6, 32, 24);
            ShaderGraphDescription graph = Record(effect);
            Rect output = graph.TransformBounds(source);
            float[] pixels = ShaderGraphContractTests.Execute(graph, source, scale).Pixels;
            int width = PixelRect.FromRect(output, scale).Width;
            for (int y = (int)(8 * scale); y < (int)(20 * scale); y++)
                for (int x = (int)(8 * scale); x < (int)(24 * scale); x++)
                {
                    float px = output.X + (x + 0.5f) / scale - source.X;
                    float py = output.Y + (y + 0.5f) / scale - source.Y;
                    float expectedR = (px + 2 * MathF.Sin(py / 16 * MathF.Tau + MathF.PI / 4)) / source.Width;
                    float expectedG = (py + 2 * MathF.Sin(px / 16 * MathF.Tau + MathF.PI / 4)) / source.Height;
                    Assert.That(pixels[(y * width + x) * 4], Is.EqualTo(expectedR).Within(0.003));
                    Assert.That(pixels[(y * width + x) * 4 + 1], Is.EqualTo(expectedG).Within(0.003));
                }
        });
    }

    [Test]
    public void PackedDataBuildsTheSameGraphWithoutGeneratedClrTypes()
    {
        byte[] initial = MemoryMarshal.AsBytes(new[] { new Vector4(0.25f, 0.5f, 0.75f, 1) }.AsSpan()).ToArray();
        byte[] constants = MemoryMarshal.AsBytes(new[] { Vector4.One }.AsSpan()).ToArray();
        ShaderGraphDescription Build(bool packed) => ShaderGraphBuilder.Build(0, (graph, _) =>
        {
            ShaderBuffer source = packed ? graph.Buffer("source-data", initial, 16)
                : graph.Buffer<Vector4>("source-data", MemoryMarshal.Cast<byte, Vector4>(initial));
            ShaderBuffer copied = packed ? graph.Buffer("copied", 1, 16) : graph.Buffer<Vector4>("copied", 1);
            graph.Copy("copy", source, copied);
            ShaderImage output = graph.Image2D("output", graph.Source);
            graph.Fullscreen("display", s_packed, pass =>
            {
                pass.StorageBuffer(0, copied, ResourceAccess.Read);
                if (packed) pass.PushConstants(constants.AsSpan());
                else pass.PushConstants(Vector4.One);
                pass.ColorAttachment(0, output);
            });
            return graph.Output(output);
        });
        ShaderGraphDescription packed = Build(true);
        Assert.That(packed.OutputIdentity, Is.EqualTo(Build(false).OutputIdentity));
        Array.Clear(initial);
        Array.Clear(constants);
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            float[] pixels = ShaderGraphContractTests.Execute(packed, new(0, 0, 4, 2)).Pixels;
            Assert.That(pixels.Take(4), Is.EqualTo(new[] { 0.25f, 0.5f, 0.75f, 1 }).Within(0.001));
        });
    }

    [TestCase(0, 16)]
    [TestCase(16, 0)]
    [TestCase(17, 16)]
    public void PackedBuffersRequireCompletePositiveElements(int bytes, int stride)
        => Assert.That(() => ShaderGraphBuilder.Build(0, (graph, _) =>
        {
            graph.Buffer("data", new byte[bytes], stride);
            return graph.Output(graph.Source);
        }), Throws.InstanceOf<ArgumentException>());

    internal static FilterEffect Create(bool refraction) => refraction
        ? new WaveRefraction { Amount = { Value = 4 }, Wavelength = { Value = 12 }, Phase = { Value = 45 } }
        : new Bloom { Radius = { Value = 4 }, Threshold = { Value = 0.05f }, SoftKnee = { Value = 50 } };

    internal static ShaderGraphDescription Record(FilterEffect effect)
    {
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(ShaderGraphRenderingTests.SourceBounds);
        context.ApplyTransactional(effect, resource);
        return ((FEItem_ShaderGraph)context.GetOrderedItems().Single()).Description;
    }

    private static readonly ShaderProgram s_packed = ShaderProgram.Fullscreen(new ShaderSource("""
        #version 450
        layout(binding = 0, std430) readonly buffer Values { vec4 value[]; } data;
        layout(push_constant) uniform Factors { vec4 value; } factor;
        layout(location = 0) out vec4 color;
        void main() { color = data.value[0] * factor.value; }
        """));
}
