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
        using var canvas = new ImmediateCanvas(target, logicalSize: s_frame, intent: RenderIntent.Delivery);

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
        using (var canvas = new ImmediateCanvas(target, logicalSize: s_frame, intent: RenderIntent.Delivery))
        {
            canvas.Clear();
            renderer.Render(canvas);
        }

        return target.Snapshot();
    }

    private static PixelSize LargestParticleLayer(float initialRotation)
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
        using var canvas = new ImmediateCanvas(target, logicalSize: s_frame, intent: RenderIntent.Delivery);
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
