using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

/// <summary>
/// Pins that every legacy filter-effect path allocates its own surfaces through the caller's
/// <see cref="IRenderTargetFactory"/> instead of the global allocator.
/// </summary>
/// <remarks>
/// A factory's targets may come from a graphics context the global allocator knows nothing about. A path that
/// goes around it both ignores the caller's allocation policy and can sample a factory-backed input into a
/// foreign surface, which shows up as missing output rather than an error. The factory is reachable only
/// through the render pass's lease session, so each seam below is checked with a session in hand.
/// </remarks>
[TestFixture]
[NonParallelizable]
public sealed class RenderTargetFactoryReachTests
{
    private const string BlueShader =
        "half4 apply(half4 color) { return half4(0.0, 0.0, color.a, color.a); }";

    private static readonly Rect s_bounds = new(0, 0, 8, 6);

    [Test]
    public void LegacyShaderStage_AllocatesThroughTheFactory()
    {
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        using ProgramCache<CachedSkRuntimeEffect> cache = SkRuntimeEffectProgramCache.Create();
        var factory = new CountingTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);

        FilterEffectStageFallbackExecutor.ApplyShader(
            targets,
            ShaderDescription.CurrentPixel(BlueShader),
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            (target, source) => SkRuntimeEffectProgramCache.AcquireForDestination(
                cache,
                target.RenderTarget!,
                source),
            session);

        Assert.That(factory.Requests, Is.Not.Empty,
            "A typed shader stage must ask the caller's factory for its output surface.");
    }

    [Test]
    public void LegacyGeometryStage_AllocatesThroughTheFactory()
    {
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        var factory = new CountingTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);

        FilterEffectStageFallbackExecutor.ApplyGeometry(
            targets,
            GeometryDescription.CreateRequestLocal(
                static session => session.Canvas.Use(static canvas => canvas.Clear()),
                RenderBoundsContract.Identity,
                RenderHitTestContract.AnyInput),
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            session);

        Assert.That(factory.Requests, Is.Not.Empty,
            "A typed geometry stage must ask the caller's factory for its output surface.");
    }

    [Test]
    public void CustomEffectTargets_AllocateThroughTheFactory()
    {
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        var factory = new CountingTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            renderTargetLeaseSession: session);

        using EffectTarget fresh = context.CreateTarget(s_bounds);
        int afterCreateTarget = factory.Requests.Count;
        using EffectTarget replacement = context.CreateTargetLike(targets[0]);

        Assert.Multiple(() =>
        {
            Assert.That(afterCreateTarget, Is.GreaterThan(0),
                "CreateTarget must ask the caller's factory.");
            Assert.That(fresh.RenderTarget, Is.Not.Null);
            Assert.That(replacement.RenderTarget, Is.Not.Null);
        });
    }

    [Test]
    public void TileBrushIntermediate_AllocatesThroughTheFactory()
    {
        var factory = new CountingTargetFactory();
        using var registry = new RenderTargetLeaseRegistry(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);
        var content = new RectShape();
        content.Width.CurrentValue = 4;
        content.Height.CurrentValue = 4;
        content.Fill.CurrentValue = Brushes.White;
        var brush = new DrawableBrush(content);
        brush.Stretch.CurrentValue = Stretch.Fill;
        using var brushResource = (Brush.Resource)brush.ToResource(CompositionContext.Default);

        var constructor = new BrushConstructor(
            s_bounds,
            brushResource,
            BlendMode.SrcOver,
            scale: 1f,
            maxWorkingScale: 1f,
            RenderIntent.Preview,
            static (_, bounds, _) => new MaterializedDrawableBrush(CreateOpaqueImage(4, 4), bounds),
            session);

        using SKShader? shader = constructor.CreateShader();

        Assert.Multiple(() =>
        {
            Assert.That(shader, Is.Not.Null, "The fixture must reach the tile-intermediate path.");
            Assert.That(factory.Requests, Is.Not.Empty,
                "A tile-brush intermediate must ask the caller's factory.");
        });
    }

    private static SKImage CreateOpaqueImage(int width, int height)
    {
        using SKSurface surface = SKSurface.Create(new SKImageInfo(
            width,
            height,
            SKColorType.RgbaF16,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgbLinear()))
            ?? throw new InvalidOperationException("The materializer fixture needs a CPU surface.");
        surface.Canvas.Clear(SKColors.White);
        return surface.Snapshot();
    }

    private static EffectTargets CreateSolidTargets(Rect bounds)
    {
        using RenderTarget renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)
            ?? throw new InvalidOperationException("A CPU render target is required for this test.");
        using (var canvas = new ImmediateCanvas(
                   renderTarget,
                   density: 1,
                   maxWorkingScale: 1,
                   logicalSize: bounds.Size))
        {
            canvas.Clear(Colors.Red);
        }

        return new EffectTargets
        {
            new EffectTarget(renderTarget, bounds, EffectiveScale.At(1)),
        };
    }

    private sealed class CountingTargetFactory : IRenderTargetFactory
    {
        public List<PixelSize> Requests { get; } = [];

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            Requests.Add(allocation.DeviceSize);
            return RenderTarget.Create(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
        }
    }
}
