using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Particles;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// ParticleRenderNode records the particle drawable once and centres and scales every particle from
// the measured source bounds. A 20x12 sprite therefore has to stay a 20x12 source however it is
// grouped: the 1920x1080 recording domain is only the canvas the sprite is recorded on.
[NonParallelizable]
[TestFixture]
public class ParticleRenderNodeSourceBoundsTests
{
    private static readonly Size s_frame = new(256, 144);
    private const float SpriteWidth = 20f;
    private const float SpriteHeight = 12f;
    private const float ParticleX = 100f;
    private const float ParticleY = 80f;

    public enum ParticleSourceKind
    {
        PlainSprite,
        PlainGroup,
        OpacityGroup,
        BlendGroup,
        ChildBlendGroup,
    }

    [TestCase(ParticleSourceKind.PlainSprite)]
    [TestCase(ParticleSourceKind.PlainGroup)]
    [TestCase(ParticleSourceKind.OpacityGroup)]
    [TestCase(ParticleSourceKind.BlendGroup)]
    [TestCase(ParticleSourceKind.ChildBlendGroup)]
    public void SmallParticleSource_KeepsTheSpriteExtentAndCenter(ParticleSourceKind kind)
    {
        using ParticleEmitter.Resource resource = BuildResource(kind);
        Assert.That(resource.GetAliveParticles().Length, Is.EqualTo(1),
            "precondition: the fixture must place exactly one particle");
        PlaceParticle(resource, ParticleX, ParticleY);

        RenderNodeMeasurement measurement = ScaleRecordingTestHelper.Measure(
            new ParticleRenderNode(resource),
            outputScale: 1,
            targetDomain: new Rect(default, s_frame));
        (Bitmap bitmap, List<PixelSize> requests) = Render(resource);
        using (bitmap)
        {
            AlphaBounds footprint = MeasureAlphaBounds(bitmap, out float maximumAlpha);
            var expected = new Rect(
                ParticleX - (SpriteWidth / 2),
                ParticleY - (SpriteHeight / 2),
                SpriteWidth,
                SpriteHeight);

            TestContext.WriteLine(
                $"{kind}: measured={measurement.OutputBounds} expected={expected} "
                + $"footprint={footprint} maxAlpha={maximumAlpha:F3} "
                + $"requests=[{string.Join(", ", requests)}]");

            Assert.Multiple(() =>
            {
                Assert.That(footprint.Width, Is.GreaterThan(0),
                    "the render must contain visible pixels; a frame-sized source moves the sprite off the frame");
                Assert.That(measurement.OutputBounds.Left, Is.EqualTo(expected.Left).Within(1f),
                    "the measured source must be centred on the particle");
                Assert.That(measurement.OutputBounds.Top, Is.EqualTo(expected.Top).Within(1f));
                Assert.That(measurement.OutputBounds.Width, Is.EqualTo(SpriteWidth).Within(1f),
                    "a 20x12 sprite must not be measured as the 1920x1080 recording domain");
                Assert.That(measurement.OutputBounds.Height, Is.EqualTo(SpriteHeight).Within(1f));

                Assert.That(footprint.Left, Is.EqualTo(expected.Left).Within(2));
                Assert.That(footprint.Top, Is.EqualTo(expected.Top).Within(2));
                Assert.That(footprint.Width, Is.EqualTo((int)SpriteWidth).Within(2));
                Assert.That(footprint.Height, Is.EqualTo((int)SpriteHeight).Within(2));
                Assert.That(footprint.Left + (footprint.Width / 2), Is.EqualTo((int)ParticleX).Within(2),
                    "the sprite must stay centred on the particle position");
                Assert.That(footprint.Top + (footprint.Height / 2), Is.EqualTo((int)ParticleY).Within(2));

                // Isolating for compositing may only change the alpha of the same footprint.
                if (kind == ParticleSourceKind.OpacityGroup)
                    Assert.That(maximumAlpha, Is.EqualTo(0.5f).Within(0.05f));
                else
                    Assert.That(maximumAlpha, Is.EqualTo(1f).Within(0.05f));

                Assert.That(requests, Is.Not.Empty, "the fixture must reach the particle layer allocation");
                Assert.That(
                    requests,
                    Has.All.Matches<PixelSize>(size =>
                        size.Width <= SpriteWidth + 4 && size.Height <= SpriteHeight + 4),
                    "a 20x12 particle source and its composite must not allocate a frame-sized layer");
            });
        }
    }

    private static ParticleEmitter.Resource BuildResource(ParticleSourceKind kind)
    {
        var sprite = new RectShape();
        // Placed at the recording canvas origin: if the source were measured as the whole 1920x1080
        // domain, the sprite would sit half a frame off its particle instead of centred on it.
        sprite.AlignmentX.Value = AlignmentX.Left;
        sprite.AlignmentY.Value = AlignmentY.Top;
        sprite.Width.Value = SpriteWidth;
        sprite.Height.Value = SpriteHeight;
        sprite.Fill.Value = Brushes.White;

        Drawable particleDrawable = sprite;
        switch (kind)
        {
            case ParticleSourceKind.PlainGroup:
                {
                    var group = new DrawableGroup();
                    group.Children.Add(sprite);
                    particleDrawable = group;
                    break;
                }
            case ParticleSourceKind.OpacityGroup:
                {
                    var group = new DrawableGroup();
                    group.Opacity.Value = 50;
                    group.Children.Add(sprite);
                    particleDrawable = group;
                    break;
                }
            case ParticleSourceKind.BlendGroup:
                {
                    var group = new DrawableGroup();
                    group.BlendMode.Value = BlendMode.Plus;
                    group.Children.Add(sprite);
                    particleDrawable = group;
                    break;
                }
            case ParticleSourceKind.ChildBlendGroup:
                {
                    // A child blend mode isolates the group even though its own opacity and blend are default.
                    sprite.BlendMode.Value = BlendMode.Plus;
                    var group = new DrawableGroup();
                    group.Children.Add(sprite);
                    particleDrawable = group;
                    break;
                }
        }

        var emitter = new ParticleEmitter
        {
            // The emitter's own placement must not add a translate to the particle positions.
            AlignmentX = { Value = AlignmentX.Left },
            AlignmentY = { Value = AlignmentY.Top },
            Seed = { Value = 11 },
            EmissionRate = { Value = 60 },
            Lifetime = { Value = 2 },
            MaxParticles = { Value = 1 },
            Speed = { Value = 0 },
            SpeedRandom = { Value = 0 },
            Gravity = { Value = 0 },
            Spread = { Value = 0 },
            ParticleSize = { Value = 10 },
            SizeRandom = { Value = 0 },
            ParticleColor = { Value = Colors.White },
            ParticleOpacity = { Value = 100 },
            EndSizeMultiplier = { Value = 1 },
            EndOpacityMultiplier = { Value = 1 },
            InitialRotation = { Value = 0 },
            InitialRotationRandom = { Value = 0 },
            AngularVelocity = { Value = 0 },
            ParticleDrawable = { Value = particleDrawable },
        };
        return (ParticleEmitter.Resource)emitter.ToResource(
            new CompositionContext(MediaTime.FromSeconds(1)));
    }

    // The simulator owns the buffer the render node reads; pinning one particle this way removes the
    // emission randomness from what the test measures.
    private static void PlaceParticle(ParticleEmitter.Resource resource, float x, float y)
    {
        Span<Particle> particles = MemoryMarshal.AsMemory(resource.GetAliveParticles()).Span;
        particles[0].X = x;
        particles[0].Y = y;
        particles[0].Rotation = 0;
        particles[0].AngularVelocity = 0;
        particles[0].CurrentSize = 10;
        particles[0].CurrentOpacity = 100;
        particles[0].CurrentColor = Colors.White;
    }

    private static (Bitmap Bitmap, List<PixelSize> Requests) Render(ParticleEmitter.Resource resource)
    {
        using var root = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(root, s_frame, outputScale: 1))
            resource.Render(context);

        var factory = new RecordingTargetFactory();
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

        return (target.Snapshot(), factory.Requests);
    }

    private static AlphaBounds MeasureAlphaBounds(Bitmap bitmap, out float maximumAlpha)
    {
        int minX = bitmap.Width;
        int minY = bitmap.Height;
        int maxX = -1;
        int maxY = -1;
        maximumAlpha = 0;
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[((y * bitmap.Width) + x) * 4 + 3]);
                if (alpha <= 0.01f)
                    continue;

                maximumAlpha = Math.Max(maximumAlpha, alpha);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < minX || maxY < minY
            ? default
            : new AlphaBounds(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private sealed class RecordingTargetFactory : IRenderTargetFactory
    {
        public List<PixelSize> Requests { get; } = [];

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize size = allocation.DeviceSize;
            Requests.Add(size);
            return CpuRenderTargets.Create(size.Width, size.Height);
        }
    }

    private readonly record struct AlphaBounds(int Left, int Top, int Width, int Height);
}
