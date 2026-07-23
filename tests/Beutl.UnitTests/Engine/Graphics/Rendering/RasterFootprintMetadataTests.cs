using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[TestFixture]
public sealed class RasterFootprintMetadataTests
{
    [Test]
    public void ExecutionInput_DrawsTheCompleteRasterFootprintWithoutChangingSemanticBounds()
    {
        const float density = 2;
        var bounds = new Rect(10.25f, 20.25f, 8, 6);
        PixelRect deviceBounds = PixelRect.FromRect(bounds, density);
        Rect rasterBounds = deviceBounds.ToRect(density);
        Rect? drawnBounds = null;
        var token = new RenderExecutionSessionToken();
        var input = new RenderExecutionInput(
            token,
            bounds,
            EffectiveScale.At(density),
            deviceBounds,
            draw: (_, destination) => drawnBounds = destination,
            drawDeviceSpace: static (_, _) => { },
            createShader: null,
            createSnapshot: null,
            readbackDeclared: false);

        using RenderTarget target = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        var canvas = new RenderCallbackCanvas(
            token,
            density,
            bounds,
            deviceBounds,
            () => new ImmediateCanvas(target, density, logicalSize: rasterBounds.Size),
            CallbackCanvasCapability.Draw);

        canvas.Use(input.Draw);

        Assert.Multiple(() =>
        {
            Assert.That(input.Bounds, Is.EqualTo(bounds));
            Assert.That(input.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(input.RasterBounds, Is.EqualTo(rasterBounds));
            Assert.That(input.LogicalOrigin, Is.EqualTo(rasterBounds.Position));
            Assert.That(drawnBounds, Is.EqualTo(rasterBounds));
        });

        token.Complete();
    }

    [Test]
    public void CallbackCanvas_UsesAnExplicitPhysicalFootprintForOriginAndClipping()
    {
        const float density = 2;
        var logicalBounds = new Rect(10.25f, 20.25f, 8, 6);
        PixelRect canonical = PixelRect.FromRect(logicalBounds, density);
        var deviceBounds = new PixelRect(
            canonical.X - 1,
            canonical.Y - 1,
            canonical.Width + 2,
            canonical.Height + 2);
        Rect rasterBounds = deviceBounds.ToRect(density);
        var token = new RenderExecutionSessionToken();
        using RenderTarget target = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        var facade = new RenderCallbackCanvas(
            token,
            density,
            logicalBounds,
            deviceBounds,
            () => new ImmediateCanvas(target, density, logicalSize: rasterBounds.Size),
            CallbackCanvasCapability.Draw);

        facade.Use(canvas =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(facade.LogicalBounds, Is.EqualTo(logicalBounds));
                Assert.That(facade.DeviceBounds, Is.EqualTo(deviceBounds));
                Assert.That(facade.RasterBounds, Is.EqualTo(rasterBounds));
                Assert.That(facade.LogicalOrigin, Is.EqualTo(rasterBounds.Position));
                Assert.That(canvas.Transform.Transform(rasterBounds.Position), Is.EqualTo(default(Point)));
            });
        });

        token.Complete();
    }

    [Test]
    public void CachedValue_PreservesThePhysicalFootprintIndependentlyOfSemanticBounds()
    {
        const float density = 2;
        var bounds = new Rect(10.25f, 20.25f, 8, 6);
        PixelRect canonical = PixelRect.FromRect(bounds, density);
        var deviceBounds = new PixelRect(
            canonical.Position,
            new PixelSize(canonical.Width + 1, canonical.Height + 2));
        using RenderTarget target = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        var value = new RenderNodeCachedValue(
            target,
            bounds,
            EffectiveScale.At(density),
            deviceBounds);

        Assert.Multiple(() =>
        {
            Assert.That(value.Bounds, Is.EqualTo(bounds));
            Assert.That(value.CompleteBounds, Is.EqualTo(bounds));
            Assert.That(value.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(value.RasterBounds, Is.EqualTo(deviceBounds.ToRect(density)));
        });
    }

    [Test]
    public void CachedValue_RejectsAPhysicalFootprintThatDoesNotContainSemanticBounds()
    {
        const float density = 2;
        var bounds = new Rect(10.25f, 20.25f, 8, 6);
        PixelRect canonical = PixelRect.FromRect(bounds, density);
        var shifted = new PixelRect(
            canonical.X + 1,
            canonical.Y,
            canonical.Width,
            canonical.Height);
        using RenderTarget target = RenderTarget.CreateNull(shifted.Width, shifted.Height);

        Assert.That(
            () => new RenderNodeCachedValue(target, bounds, EffectiveScale.At(density), shifted),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("deviceBounds"));
    }

    [Test]
    public void EffectTarget_TranslatesRasterBoundsWithoutMutatingItsAllocationFootprint()
    {
        const float density = 2;
        var bounds = new Rect(10.25f, 20.25f, 8, 6);
        PixelRect deviceBounds = PixelRect.FromRect(bounds, density);
        using RenderTarget renderTarget = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        using var target = new EffectTarget(
            renderTarget,
            bounds,
            EffectiveScale.At(density),
            deviceBounds);
        Rect initialRasterBounds = deviceBounds.ToRect(density);
        var translation = new Vector(3.25f, -1.5f);

        target.Bounds = target.Bounds.Translate(translation);
        using EffectTarget clone = target.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(target.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(target.RasterBounds, Is.EqualTo(initialRasterBounds.Translate(translation)));
            Assert.That(target.RasterBounds.Size, Is.EqualTo(initialRasterBounds.Size));
            Assert.That(clone.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(clone.Bounds, Is.EqualTo(target.Bounds));
            Assert.That(clone.RasterBounds, Is.EqualTo(target.RasterBounds));
        });
    }

    [Test]
    public void DeviceBufferBounds_IncludesFractionalOriginRoundingPixels()
    {
        const float density = 2;
        var bounds = new Rect(10.25f, 20.25f, 8, 6);

        PixelRect actual = CustomFilterEffectContext.DeviceBufferBounds(bounds, density);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.EqualTo(PixelRect.FromRect(bounds, density)));
            Assert.That(actual.Size, Is.EqualTo(new PixelSize(17, 13)));
            Assert.That(
                CustomFilterEffectContext.DeviceBufferSize(bounds, density),
                Is.EqualTo((actual.Width, actual.Height)));
        });
    }
}
