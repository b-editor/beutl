using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

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
            Seed = { CurrentValue = 1234 },
            EmissionRate = { CurrentValue = 24 },
            Lifetime = { CurrentValue = 1.2f },
            MaxParticles = { CurrentValue = 400 },
            Speed = { CurrentValue = speed },
            SpeedRandom = { CurrentValue = speedRandom },
            Gravity = { CurrentValue = 0 },
            Spread = { CurrentValue = spread },
            ParticleSize = { CurrentValue = 14 },
            ParticleColor = { CurrentValue = Colors.OrangeRed },
        };
        using ParticleEmitter.Resource resource = emitter.ToResource(
            new CompositionContext(TimeSpan.FromSeconds(1)));
        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_frame, outputScale: 1))
            emitter.Render(context, resource);

        var factory = new BoundedTargetFactory(maximumDimension: 512);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(default, s_frame),
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = factory,
            });
        using var target = new CpuRenderTarget((int)s_frame.Width, (int)s_frame.Height);
        using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, logicalSize: s_frame);

        Assert.That(() => renderer.Render(canvas), Throws.Nothing);
        Assert.That(factory.Requests, Has.All.Matches<PixelSize>(size =>
            size.Width <= factory.MaximumDimension && size.Height <= factory.MaximumDimension));
    }

    /// <remarks>
    /// A particle is scaled and then rotated about the source's own centre, so a turned square reaches further
    /// along both axes than the square itself. This rectangle is what the layer buffer is allocated from, so a
    /// bound that ignores rotation clips the corners off every particle instead of merely mismeasuring them.
    /// </remarks>
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

    /// <remarks>
    /// A particle is drawn through its own scale and rotation, so the blit resamples the source. Point
    /// sampling replicates whichever texels the sample points land on, so the edge steps through a handful of
    /// repeated alphas instead of a gradient - the count of distinct edge alphas separates the two.
    /// </remarks>
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
            // Point sampling replicates whichever source texels the sample points land on, so a magnified
            // particle's edge repeats a handful of alphas; measured here it was 81 against 314 resampled.
            Assert.That(distinct, Has.Count.GreaterThan(150),
                "A magnified particle's edge must be resampled, not stepped through repeated source texels.");
        });
    }

    /// <remarks>
    /// <see cref="ParticleEmitter.EndOpacityMultiplier"/> defaults to zero, so fading to
    /// <c>CurrentOpacity == 0</c> is an ordinary state for a still-alive particle. The drawing loop skips such a
    /// particle, and the bounds loop has to skip it on the same predicate: the union is what the layer buffer is
    /// allocated from, so a distant invisible particle otherwise buys a buffer that renders nothing.
    /// </remarks>
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

    /// <remarks>
    /// The drawing loop rejects a non-finite opacity, not merely a non-positive one - <c>NaN &lt;= 0</c> is false,
    /// so a bare sign test would let a NaN opacity through and hand the union an unbounded coordinate.
    /// </remarks>
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

    /// <remarks>
    /// The opposite side of the same predicate: an opacity that is merely faint still reaches the canvas, so the
    /// layer has to cover it. Only a non-positive or non-finite opacity may be dropped. The check is applied to
    /// the normalized opacity, so the value here has to stay positive after the divide by 100.
    /// </remarks>
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

    /// <remarks>
    /// The liveness and scale filters the bounds loop already applied must keep working alongside the opacity one.
    /// </remarks>
    [Test]
    public void DeadOrZeroSizedParticles_RemainExcludedFromTheAllocatedBounds()
    {
        PixelSize clustered = LargestParticleLayer(initialRotation: 0f);
        PixelSize farDead = LargestParticleLayer(
            initialRotation: 0f,
            mutate: static particles =>
            {
                particles[0].X = FarOffset;
                particles[0].IsAlive = false;
            });
        PixelSize farZeroSized = LargestParticleLayer(
            initialRotation: 0f,
            mutate: static particles =>
            {
                particles[0].X = FarOffset;
                particles[0].CurrentSize = 0f;
            });

        Assert.Multiple(() =>
        {
            Assert.That(farDead, Is.EqualTo(clustered), "A dead particle stays out of the union.");
            Assert.That(farZeroSized, Is.EqualTo(clustered), "A zero-sized particle stays out of the union.");
        });
    }

    private static Bitmap RenderParticles(float initialRotation, float particleSize)
    {
        var emitter = new ParticleEmitter
        {
            Seed = { CurrentValue = 11 },
            EmissionRate = { CurrentValue = 4 },
            Lifetime = { CurrentValue = 1.2f },
            MaxParticles = { CurrentValue = 8 },
            Speed = { CurrentValue = 0 },
            SpeedRandom = { CurrentValue = 0 },
            Gravity = { CurrentValue = 0 },
            Spread = { CurrentValue = 0 },
            ParticleSize = { CurrentValue = particleSize },
            ParticleColor = { CurrentValue = Colors.White },
            InitialRotation = { CurrentValue = initialRotation },
            InitialRotationRandom = { CurrentValue = 0 },
        };
        using ParticleEmitter.Resource resource = emitter.ToResource(
            new CompositionContext(TimeSpan.FromSeconds(1)));
        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_frame, outputScale: 1))
            emitter.Render(context, resource);

        var factory = new BoundedTargetFactory(maximumDimension: 4096);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(default, s_frame),
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = factory,
            });
        using var target = new CpuRenderTarget((int)s_frame.Width, (int)s_frame.Height);
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
            Seed = { CurrentValue = 7 },
            EmissionRate = { CurrentValue = 4 },
            Lifetime = { CurrentValue = 1.2f },
            MaxParticles = { CurrentValue = 8 },
            Speed = { CurrentValue = 0 },
            SpeedRandom = { CurrentValue = 0 },
            Gravity = { CurrentValue = 0 },
            Spread = { CurrentValue = 0 },
            ParticleSize = { CurrentValue = 40 },
            ParticleColor = { CurrentValue = Colors.OrangeRed },
            InitialRotation = { CurrentValue = initialRotation },
            InitialRotationRandom = { CurrentValue = 0 },
        };
        using ParticleEmitter.Resource resource = emitter.ToResource(
            new CompositionContext(TimeSpan.FromSeconds(1)));
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
            emitter.Render(context, resource);

        var factory = new BoundedTargetFactory(maximumDimension: 4096);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    Intent = RenderIntent.Delivery,
                    TargetDomain = new Rect(default, s_frame),
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = RenderCacheOptions.Disabled,
                    Purpose = RenderRequestPurpose.Frame,
                },
                TargetFactory = factory,
            });
        using var target = new CpuRenderTarget((int)s_frame.Width, (int)s_frame.Height);
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
                ? new CpuRenderTarget(size.Width, size.Height)
                : null;
        }
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(CreateSurface(width, height), width, height);

    private static SKSurface CreateSurface(int width, int height)
        => SKSurface.Create(new SKImageInfo(
               width,
               height,
               SKColorType.RgbaF16,
               SKAlphaType.Premul,
               SKColorSpace.CreateSrgbLinear()))
           ?? throw new InvalidOperationException("Could not create a CPU render target.");
}
