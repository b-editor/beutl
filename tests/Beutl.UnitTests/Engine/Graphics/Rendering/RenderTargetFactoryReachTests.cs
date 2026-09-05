using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
[NonParallelizable]
public sealed class RenderTargetFactoryReachTests
{
    private const string BlueShader =
        "half4 apply(half4 color) { return half4(0.0, 0.0, color.a, color.a); }";

    private static readonly Rect s_bounds = new(0, 0, 8, 6);

    [Test]
    public void EffectItemShaderStage_AllocatesThroughTheFactory()
    {
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        using ProgramCache<CachedSkRuntimeEffect> cache = SkRuntimeEffectProgramCache.Create();
        var factory = new CountingTargetFactory();
        using var registry = new RenderTargetPool(factory);
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
    public void EffectItemGeometryStage_AllocatesThroughTheFactory()
    {
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        var factory = new CountingTargetFactory();
        using var registry = new RenderTargetPool(factory);
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
        using var registry = new RenderTargetPool(factory);
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
    public void ADeclinedNativeReplacement_MarksTheRequestAsHavingDroppedContent()
    {
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        var factory = new DecliningTargetFactory();
        using var registry = new RenderTargetPool(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Preview);
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            renderTargetLeaseSession: session);

        using EffectTarget replacement = context.CreateNativeTargetLike(targets[0]);

        Assert.Multiple(() =>
        {
            Assert.That(factory.Declined, Is.GreaterThan(0), "the fixture must actually decline");
            Assert.That(replacement.RenderTarget, Is.Null, "a declined replacement is an empty target");
            Assert.That(
                session.ContentDropObserved,
                Is.True,
                "the preview kept the unfiltered source, so the request dropped content");
        });
    }

    [Test]
    public void ADeclinedNativeReplacement_FailsADeliveryRenderInsteadOfDroppingTheEffect()
    {
        using EffectTargets targets = CreateSolidTargets(s_bounds);
        var factory = new DecliningTargetFactory();
        using var registry = new RenderTargetPool(factory);
        using RenderTargetLeaseSession session = registry.BeginSession(RenderIntent.Delivery);
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Delivery,
            RenderRequestPurpose.Frame,
            outputScale: 1,
            workingScale: 1,
            maxWorkingScale: 1,
            renderTargetLeaseSession: session);

        Assert.Multiple(() =>
        {
            Assert.That(
                () => context.CreateNativeTargetLike(targets[0]),
                Throws.TypeOf<InvalidOperationException>()
                    .With.Message.Contains("could not allocate"));
            Assert.That(
                session.ContentDropObserved,
                Is.False,
                "a delivery render fails rather than recording a drop and carrying on");
        });
    }

    [Test]
    public void TileBrushIntermediate_AllocatesThroughTheFactory()
    {
        var factory = new CountingTargetFactory();
        using var registry = new RenderTargetPool(factory);
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
                   RenderIntent.Preview,
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

    private sealed class DecliningTargetFactory : IRenderTargetFactory
    {
        public int Declined { get; private set; }

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            Declined++;
            return null;
        }
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
