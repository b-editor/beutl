using Beutl.Graphics;
using Beutl.Graphics.Particles;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public class SubPixelParticlesStayVisibleTests
{
    [Test]
    public void ParticleCoverage_GrowsWithParticleSizeThroughTheSubPixelRange()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            // A particle's source is ten units across, so these sizes span 0.04x to 0.2x — every one of
            // them lands under a device pixel at output scale 1.
            float[] sizes = [0.4f, 0.6f, 0.8f, 1.0f, 1.5f, 2.0f];
            double[] coverage = [.. sizes.Select(MeasureCoverage)];

            Assert.That(
                coverage[0],
                Is.GreaterThan(0),
                "a sub-pixel particle still covers part of a pixel, so the emitter must not render an "
                + "empty frame.");
            for (int i = 1; i < coverage.Length; i++)
            {
                Assert.That(
                    coverage[i],
                    Is.GreaterThan(coverage[i - 1]),
                    $"a particle of size {sizes[i]} covers more than one of size {sizes[i - 1]}, so "
                    + "coverage has to follow the size rather than fall off a threshold.");
            }
        });
    }

    private static double MeasureCoverage(float particleSize)
    {
        var emitter = new ParticleEmitter();
        // The simulator seeds its Random from this, so an unpinned seed would make the measurement noise.
        emitter.Seed.CurrentValue = 1234;
        emitter.EmissionRate.CurrentValue = 24f;
        emitter.Lifetime.CurrentValue = 1.2f;
        emitter.MaxParticles.CurrentValue = 400;
        emitter.Speed.CurrentValue = 150f;
        emitter.Gravity.CurrentValue = 200f;
        emitter.Spread.CurrentValue = 40f;
        emitter.ParticleSize.CurrentValue = particleSize;
        emitter.ParticleColor.CurrentValue = Colors.OrangeRed;

        using Bitmap bitmap = SceneRenderHarness.RenderSingle(
            emitter,
            new PixelSize(256, 144),
            1f,
            2f,
            TimeSpan.FromSeconds(1));
        return SceneRenderHarness.SumAlpha(bitmap);
    }
}
