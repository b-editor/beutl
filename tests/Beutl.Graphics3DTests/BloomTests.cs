using System.Numerics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;
using Beutl.Serialization;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class BloomTests
{
    [TestCase(1f)]
    [TestCase(0.25f)]
    public void SoftThresholdUsesStraightLuminanceAndPreservesPremultiplication(float alpha)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var cache = ShaderGraphPipeline.CreateCache();
            // Known contributions below, within, and above a threshold of 0.5, including HDR.
            (float Softness, float Luminance, float Light)[] cases =
            [
                (0, 0.375f, 0), (0, 0.5f, 0), (0, 0.75f, 0.25f),
                (50, 0.125f, 0), (50, 0.25f, 0), (50, 0.375f, 0.015625f),
                (50, 0.5f, 0.0625f), (50, 0.625f, 0.140625f), (50, 0.75f, 0.25f), (50, 4, 3.5f),
                (100, 0.25f, 0.03125f), (100, 0.5f, 0.125f), (100, 1, 0.5f),
            ];
            foreach ((float softness, float luminance, float light) in cases)
            {
                var effect = new Bloom { Radius = { Value = 0 }, SoftKnee = { Value = softness } };
                var source = new Vector4(luminance * alpha, luminance * alpha, luminance * alpha, alpha);
                float[] actual = ShaderGraphContractTests.Execute(ShaderGraphEffectTests.Record(effect), new(0, 0, 4, 4),
                    sourceColor: source, cache: cache).Pixels;
                float rgb = (luminance + light) * alpha;
                float outputAlpha = alpha + alpha * light / luminance * (1 - alpha);
                // The two RGBA16F blur targets round at the magnitude of the HDR output.
                Assert.That(actual.Take(4), Is.EqualTo(new[] { rgb, rgb, rgb, outputAlpha }).Within(Math.Max(0.001f, rgb / 1024)),
                    $"Softness={softness}, luminance={luminance}, alpha={alpha}");
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ZeroThresholdAndBlackOrTransparentPixelsRemainFinite(bool preserveAlpha)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var effect = new Bloom
            {
                Radius = { Value = 0 },
                Threshold = { Value = 0 },
                SoftKnee = { Value = 100 },
                PreserveAlpha = { Value = preserveAlpha },
            };
            ShaderGraphDescription graph = ShaderGraphEffectTests.Record(effect);
            using var cache = ShaderGraphPipeline.CreateCache();
            foreach (Vector4 source in new[] { Vector4.Zero, new Vector4(0, 0, 0, 1), new Vector4(0.25f) })
            {
                float[] actual = ShaderGraphContractTests.Execute(graph, new(0, 0, 4, 4), sourceColor: source, cache: cache).Pixels;
                Assert.That(actual.All(float.IsFinite), Is.True);
                Vector4 expected = source.X == 0 ? source : preserveAlpha
                    ? new Vector4(0.3125f, 0.3125f, 0.3125f, 0.25f)
                    : new Vector4(0.5f, 0.5f, 0.5f, 0.4375f);
                Assert.That(actual.Take(4), Is.EqualTo(new[] { expected.X, expected.Y, expected.Z, expected.W }).Within(0.002));
            }
        });
    }

    [Test]
    public void SoftnessIsPersistedAndSnapshottedWhileOldSettingsKeepTheirAppearance()
    {
        var effect = new Bloom { SoftKnee = { Value = 65 } };
        ShaderGraphDescription before = ShaderGraphEffectTests.Record(effect);
        var saved = CoreSerializer.SerializeToJsonObject(effect);
        var loaded = (Bloom)CoreSerializer.DeserializeFromJsonObject(saved, typeof(Bloom));
        Assert.That(loaded.SoftKnee.Value, Is.EqualTo(65));
        Assert.That(ShaderGraphEffectTests.Record(loaded).OutputIdentity, Is.EqualTo(before.OutputIdentity));

        effect.SoftKnee.Value = 20;
        ShaderGraphDescription after = ShaderGraphEffectTests.Record(effect);
        Assert.That(after.OutputIdentity, Is.Not.EqualTo(before.OutputIdentity));
        Assert.That(after.StructuralIdentity, Is.EqualTo(before.StructuralIdentity));
        Assert.That(ShaderGraphEffectTests.Record(loaded).OutputIdentity, Is.EqualTo(before.OutputIdentity));

        Assert.That(saved.Remove(nameof(Bloom.SoftKnee)), Is.True);
        var legacy = (Bloom)CoreSerializer.DeserializeFromJsonObject(saved, typeof(Bloom));
        Assert.That(legacy.SoftKnee.Value, Is.Zero);
        Assert.That(ShaderGraphEffectTests.Record(legacy).OutputIdentity,
            Is.EqualTo(ShaderGraphEffectTests.Record(new Bloom()).OutputIdentity));
    }
}
