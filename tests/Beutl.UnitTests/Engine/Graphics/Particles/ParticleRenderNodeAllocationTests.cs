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
