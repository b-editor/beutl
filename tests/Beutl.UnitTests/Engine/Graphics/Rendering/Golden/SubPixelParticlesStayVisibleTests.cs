using Beutl.Graphics;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

/// <summary>
/// A particle is a copy of its source scaled by the particle's own size, so below ten units it is a
/// minification. Filling a rectangle with the source's shader made that minification the tile mode's
/// problem: a decal domain narrower than the sample footprint drops out, and the emitter rendered an
/// entirely empty frame while the same emitter drawn larger rendered normally.
/// </summary>
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

        var scene = new Scene(256, 144, "particles") { Uri = new Uri("file:///particles/scene") };
        var element = new Element
        {
            Start = TimeSpan.Zero,
            Length = TimeSpan.FromSeconds(60),
            ZIndex = 0,
            IsEnabled = true,
            Uri = new Uri("file:///particles/element"),
        };
        element.AddObject(emitter);
        scene.Children.Add(element);

        using var renderer = new SceneRenderer(scene, 1f, false, 2f)
        {
            CacheOptions = RenderCacheOptions.Disabled,
        };
        renderer.Render(renderer.Compositor.EvaluateGraphics(TimeSpan.FromSeconds(1)));
        using Bitmap bitmap = renderer.Snapshot();

        double coverage = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y)[..(bitmap.Width * 4)];
            for (int x = 3; x < row.Length; x += 4)
                coverage += (float)BitConverter.UInt16BitsToHalf(row[x]);
        }

        return coverage;
    }
}
