using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics.Particles;

[TestFixture]
public sealed class ParticleRenderNodeAllocationTests
{
    private static readonly Size s_frame = new(256, 144);

    // Far enough that a particle counted in the union visibly enlarges the layer, close enough that the
    // inflated buffer still allocates under the fixture's dimension cap.
    private const float FarOffset = 1000f;

    private delegate void ParticleMutator(Span<Particle> particles);

    [TestCase(9_000f, 9_000f, 360f)]
    [TestCase(100_000f, 0f, 40f)]
    public void FastOffFrameParticles_DoNotAllocateTheirFullUnionBounds(
        float speed,
        float speedRandom,
        float spread)
    {
        var emitter = new ParticleEmitter
        {
            Seed = { Value = 1234 },
            EmissionRate = { Value = 24 },
            Lifetime = { Value = 1.2f },
            MaxParticles = { Value = 400 },
            Speed = { Value = speed },
            SpeedRandom = { Value = speedRandom },
            Gravity = { Value = 0 },
            Spread = { Value = spread },
            ParticleSize = { Value = 14 },
            ParticleColor = { Value = Colors.OrangeRed },
        };
        using ParticleEmitter.Resource resource = emitter.ToResource(
            new CompositionContext(MediaTime.FromSeconds(1)));
        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_frame, outputScale: 1))
            resource.Render(context);

        var factory = new BoundedTargetFactory(maximumDimension: 512);
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = new Rect(default, s_frame),
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, factory);
        using var target = CpuRenderTargets.Create((int)s_frame.Width, (int)s_frame.Height);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, logicalSize: s_frame);

        Assert.That(() => renderer.Render(canvas), Throws.Nothing);
        Assert.That(factory.Requests, Has.All.Matches<PixelSize>(size =>
            size.Width <= factory.MaximumDimension && size.Height <= factory.MaximumDimension));
    }

    [Test]
    public void RotatedParticles_AllocateMoreThanTheUnrotatedFootprint()
    {
        PixelSize unrotated = LargestParticleLayer(initialRotation: 0f);
        PixelSize rotated = LargestParticleLayer(initialRotation: 45f);

        Assert.Multiple(() =>
        {
            Assert.That(rotated.Width, Is.GreaterThan(unrotated.Width),
                "A 45 degree turn widens a particle's extent; the layer has to follow.");
            Assert.That(rotated.Height, Is.GreaterThan(unrotated.Height));
        });
    }

    [Test]
    public void ScaledParticles_AreResampledRatherThanPointSampled()
    {
        using Bitmap rendered = RenderParticles(initialRotation: 30f, particleSize: 37f);

        var distinct = new HashSet<ushort>();
        int fractional = 0;
        ReadOnlySpan<ushort> pixels = rendered.GetPixelSpan<ushort>();
        for (int index = 3; index < pixels.Length; index += 4)
        {
            float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[index]);
            if (alpha > 0.02f && alpha < 0.98f)
            {
                fractional++;
                distinct.Add(pixels[index]);
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(fractional, Is.GreaterThan(0), "The fixture must draw a partially covered edge.");
            // Point sampling repeats a handful of edge alphas: measured 81 distinct, against 314 resampled.
            Assert.That(distinct, Has.Count.GreaterThan(150),
                "A magnified particle's edge must be resampled, not stepped through repeated source texels.");
        });
    }

    [Test]
    public void PrewarmedParticles_RenderAtTheFirstFrameLikeAnElapsedFrame()
    {
        using Bitmap expected = RenderParticles(initialRotation: 30f, particleSize: 37f);
        using Bitmap actual = RenderParticles(initialRotation: 30f, particleSize: 37f, time: 0, prewarm: 1);
        using Bitmap empty = RenderParticles(initialRotation: 30f, particleSize: 37f, time: 0);

        Assert.That(actual.GetPixelSpan().ToArray(), Is.Not.EqualTo(empty.GetPixelSpan().ToArray()));
        Assert.That(actual.GetPixelSpan().ToArray(), Is.EqualTo(expected.GetPixelSpan().ToArray()));
    }

    [Test]
    public void TransparentParticles_AreExcludedFromTheAllocatedBounds()
    {
        PixelSize clustered = LargestParticleLayer(initialRotation: 0f);
        PixelSize farOpaque = LargestParticleLayer(
            initialRotation: 0f,
            mutate: static particles => particles[0].X = FarOffset);
        PixelSize farTransparent = LargestParticleLayer(
            initialRotation: 0f,
            mutate: static particles =>
            {
                particles[0].X = FarOffset;
                particles[0].CurrentOpacity = 0f;
            });

        Assert.Multiple(() =>
        {
            Assert.That(farOpaque.Width, Is.GreaterThan(clustered.Width),
                "The fixture must inflate the union while the distant particle still draws.");
            Assert.That(farTransparent, Is.EqualTo(clustered),
                "A fully transparent particle draws nothing, so it must not enlarge the layer.");
        });
    }

    [Test]
    public void NonFiniteOpacityParticles_AreExcludedFromTheAllocatedBounds()
    {
        PixelSize clustered = LargestParticleLayer(initialRotation: 0f);
        PixelSize farNaN = LargestParticleLayer(
            initialRotation: 0f,
            mutate: static particles =>
            {
                particles[0].X = FarOffset;
                particles[0].CurrentOpacity = float.NaN;
            });

        Assert.That(farNaN, Is.EqualTo(clustered),
            "A NaN opacity is never drawn, so it must not enlarge the layer either.");
    }

    [Test]
    public void FaintButVisibleParticles_StillEnlargeTheAllocatedBounds()
    {
        PixelSize farOpaque = LargestParticleLayer(
            initialRotation: 0f,
            mutate: static particles => particles[0].X = FarOffset);
        PixelSize farFaint = LargestParticleLayer(
            initialRotation: 0f,
            mutate: static particles =>
            {
                particles[0].X = FarOffset;
                particles[0].CurrentOpacity = 0.01f;
            });

        Assert.That(farFaint, Is.EqualTo(farOpaque),
            "A positive opacity still draws, however faint, so its extent stays in the union.");
    }

    [Test]
    public void ZeroSizedParticles_RemainExcludedFromTheAllocatedBounds()
    {
        PixelSize clustered = LargestParticleLayer(initialRotation: 0f);
        PixelSize farZeroSized = LargestParticleLayer(
            initialRotation: 0f,
            mutate: static particles =>
            {
                particles[0].X = FarOffset;
                particles[0].CurrentSize = 0f;
            });

        Assert.That(farZeroSized, Is.EqualTo(clustered), "A zero-sized particle stays out of the union.");
    }

    private static Bitmap RenderParticles(float initialRotation, float particleSize, double time = 1, double prewarm = 0)
    {
        var emitter = new ParticleEmitter
        {
            Seed = { Value = 11 },
            PrewarmDuration = { Value = MediaTime.FromSeconds(prewarm) },
            EmissionRate = { Value = 4 },
            Lifetime = { Value = 1.2f },
            MaxParticles = { Value = 8 },
            Speed = { Value = 0 },
            SpeedRandom = { Value = 0 },
            Gravity = { Value = 0 },
            Spread = { Value = 0 },
            ParticleSize = { Value = particleSize },
            ParticleColor = { Value = Colors.White },
            InitialRotation = { Value = initialRotation },
            InitialRotationRandom = { Value = 0 },
        };
        using ParticleEmitter.Resource resource = emitter.ToResource(
            new CompositionContext(MediaTime.FromSeconds(time)));
        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_frame, outputScale: 1))
            resource.Render(context);

        var factory = new BoundedTargetFactory(maximumDimension: 4096);
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = new Rect(default, s_frame),
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, factory);
        using var target = CpuRenderTargets.Create((int)s_frame.Width, (int)s_frame.Height);
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, logicalSize: s_frame))
        {
            canvas.Clear();
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    private static PixelSize LargestParticleLayer(float initialRotation, ParticleMutator? mutate = null)
    {
        var emitter = new ParticleEmitter
        {
            Seed = { Value = 7 },
            EmissionRate = { Value = 4 },
            Lifetime = { Value = 1.2f },
            MaxParticles = { Value = 8 },
            Speed = { Value = 0 },
            SpeedRandom = { Value = 0 },
            Gravity = { Value = 0 },
            Spread = { Value = 0 },
            ParticleSize = { Value = 40 },
            ParticleColor = { Value = Colors.OrangeRed },
            InitialRotation = { Value = initialRotation },
            InitialRotationRandom = { Value = 0 },
        };
        using ParticleEmitter.Resource resource = emitter.ToResource(
            new CompositionContext(MediaTime.FromSeconds(1)));
        if (mutate is not null)
        {
            // The simulator owns the buffer the render node reads, so poking it here is how a fixture
            // reaches a per-particle state the emitter's own properties can only produce for all of them.
            Span<Particle> particles = MemoryMarshal.AsMemory(resource.GetAliveParticles()).Span;
            Assert.That(particles.Length, Is.GreaterThan(1),
                "The fixture must emit several particles so one can be singled out.");
            mutate(particles);
        }

        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_frame, outputScale: 1))
            resource.Render(context);

        var factory = new BoundedTargetFactory(maximumDimension: 4096);
        using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Delivery,
            TargetDomain = new Rect(default, s_frame),
            OutputScale = 1,
            MaxWorkingScale = 1,
            CacheOptions = RenderCacheOptions.Disabled,
            Purpose = RenderRequestPurpose.Frame,
        }, factory);
        using var target = CpuRenderTargets.Create((int)s_frame.Width, (int)s_frame.Height);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, logicalSize: s_frame);
        renderer.Render(canvas);

        Assert.That(factory.Requests, Is.Not.Empty, "The fixture must reach the particle layer allocation.");
        return factory.Requests
            .OrderByDescending(static size => (long)size.Width * size.Height)
            .First();
    }

    private sealed class BoundedTargetFactory(int maximumDimension) : IRenderTargetFactory
    {
        public int MaximumDimension { get; } = maximumDimension;

        public List<PixelSize> Requests { get; } = [];

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize size = allocation.DeviceSize;
            Requests.Add(size);
            return size.Width <= MaximumDimension && size.Height <= MaximumDimension
                ? CpuRenderTargets.Create(size.Width, size.Height)
                : null;
        }
    }
}
