using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
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
    public void TargetAttachedCallback_DrawDeviceSpaceUsesTheBackingSurfaceOrigin()
    {
        var callbackBounds = new Rect(10, 12, 8, 6);
        var token = new RenderExecutionSessionToken();
        Point? observedLocalPoint = null;
        var input = new RenderExecutionInput(
            token,
            new Rect(0, 0, 2, 2),
            EffectiveScale.At(1),
            draw: static (_, _) => { },
            drawDeviceSpace: (_, point) => observedLocalPoint = point,
            createShader: null,
            createSnapshot: null,
            readbackDeclared: false);
        using RenderTarget target = RenderTarget.CreateNull(64, 48);
        var facade = new RenderCallbackCanvas(
            token,
            density: 1,
            callbackBounds,
            () => new ImmediateCanvas(target, logicalSize: new Size(64, 48)),
            CallbackCanvasCapability.TargetCommandRegion,
            mapLogicalOrigin: false);

        facade.Use(canvas => input.DrawDeviceSpace(canvas, new Point(20, 30)));

        Assert.That(observedLocalPoint, Is.EqualTo(new Point(20, 30)));
        token.Complete();
    }

    [Test]
    public void TargetAttachedCallback_ReportsTheAmbientTranslationDeviceGrid()
    {
        var callbackBounds = new Rect(10, 12, 8, 6);
        var expectedOffset = new Vector(0.25f, 0.75f);
        var token = new RenderExecutionSessionToken();
        using RenderTarget target = RenderTarget.CreateNull(64, 48);
        using var destination = new ImmediateCanvas(target, logicalSize: new Size(64, 48));
        using (destination.PushTransform(Matrix.CreateTranslation(expectedOffset)))
        {
            RenderCallbackCanvas facade = RenderCallbackCanvas.CreateTargetAttached(
                token,
                callbackBounds,
                destination,
                CallbackCanvasCapability.TargetCommandRegion);

            Assert.Multiple(() =>
            {
                Assert.That(facade.DeviceGridOffset, Is.EqualTo(expectedOffset));
                Assert.That(
                    facade.RasterBounds,
                    Is.EqualTo(facade.DeviceBounds.ToRect(facade.Density).Translate(-expectedOffset)));
                Assert.That(facade.RasterBounds.Contains(callbackBounds), Is.True);
            });
        }

        token.Complete();
    }

    [Test]
    public void TargetAttachedCallback_AcceptsRoundingNoiseAcrossLargeDeviceTranslation()
    {
        var callbackBounds = new Rect(
            -0.025896728f,
            -3.2809492E-06f,
            150.05179f,
            110);
        var translation = new Vector(49.97410583f, 70);
        var token = new RenderExecutionSessionToken();
        using RenderTarget target = RenderTarget.CreateNull(256, 192);
        using var destination = new ImmediateCanvas(target, logicalSize: new Size(256, 192));
        using (destination.PushTransform(Matrix.CreateTranslation(translation)))
        {
            RenderCallbackCanvas facade = RenderCallbackCanvas.CreateTargetAttached(
                token,
                callbackBounds,
                destination,
                CallbackCanvasCapability.TargetScope);

            PixelRect alignedLogicalBounds = PixelRect.FromRect(
                callbackBounds.Translate(facade.DeviceGridOffset),
                facade.Density);
            Assert.That(facade.DeviceBounds.Contains(alignedLogicalBounds), Is.True);
        }

        token.Complete();
    }

    [Test]
    public void TargetAttachedTargetScope_ClipsTheRasterApronOnTheAmbientDeviceGrid()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var callbackBounds = new Rect(10, 4, 4, 4);
            var gridOffset = new Vector(0.25f, 0.75f);
            var token = new RenderExecutionSessionToken();
            using RenderTarget target = RenderTarget.Create(32, 16)
                ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            using var destination = new ImmediateCanvas(target, logicalSize: new Size(32, 16));
            destination.Clear();
            PixelRect expectedBounds;
            using (destination.PushTransform(Matrix.CreateTranslation(gridOffset)))
            {
                RenderCallbackCanvas facade = RenderCallbackCanvas.CreateTargetAttached(
                    token,
                    callbackBounds,
                    destination,
                    CallbackCanvasCapability.TargetScope);
                expectedBounds = RenderScaleUtilities.AddRasterApron(facade.DeviceBounds);
                using var paint = new SKPaint { Color = SKColors.White };
                var session = new TargetScopeSession(
                    token,
                    callbackBounds,
                    callbackBounds,
                    RenderIntent.Preview,
                    RenderRequestPurpose.Frame,
                    facade,
                    [],
                    canvas => canvas.Canvas.DrawRect(SKRect.Create(32, 16), paint));

                facade.Use(_ => session.ReplayInput());
                session.ValidateCompletion();
            }

            token.Complete();
            using Bitmap bitmap = target.Snapshot();

            Assert.That(MeasureAlphaBounds(bitmap), Is.EqualTo(expectedBounds));
        });
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

    [Test]
    public void ResolveTargetDensity_ClampsTheGridAdjustedPhysicalFootprint()
    {
        var sourceBounds = new Rect(0, 0, 1, 1);
        var gridOffset = new Vector(0.5f, 0);
        PixelRect sourceDeviceBounds = PixelRect.FromRect(
            sourceBounds.Translate(gridOffset),
            1);
        using RenderTarget backing = RenderTarget.CreateNull(
            sourceDeviceBounds.Width,
            sourceDeviceBounds.Height);
        using var source = new EffectTarget(
            backing,
            sourceBounds,
            EffectiveScale.At(1),
            sourceDeviceBounds,
            gridOffset);
        using var targets = new EffectTargets { source.Clone() };
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame);
        var requestedBounds = new Rect(
            0,
            0,
            RenderScaleUtilities.MaxBufferDimension,
            1);

        float density = context.ResolveTargetDensity(requestedBounds);
        PixelRect allocated = PixelRect.FromRect(
            requestedBounds.Translate(context.DeviceGridOffset),
            density);

        Assert.Multiple(() =>
        {
            Assert.That(density, Is.LessThan(1));
            Assert.That(allocated.Width, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(allocated.Height, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
        });
    }

    [Test]
    public void CustomFilterContext_AllowsInputsFromDifferentDeviceGrids()
    {
        var bounds = new Rect(0, 0, 8, 6);
        var firstOffset = new Vector(0.25f, 0);
        var secondOffset = new Vector(0.75f, 0);
        var ambientOffset = new Vector(0.5f, 0.5f);
        PixelRect firstDeviceBounds = PixelRect.FromRect(bounds.Translate(firstOffset), 1);
        PixelRect secondDeviceBounds = PixelRect.FromRect(bounds.Translate(secondOffset), 1);
        using RenderTarget firstBacking = RenderTarget.CreateNull(
            firstDeviceBounds.Width,
            firstDeviceBounds.Height);
        using RenderTarget secondBacking = RenderTarget.CreateNull(
            secondDeviceBounds.Width,
            secondDeviceBounds.Height);
        using var targets = new EffectTargets
        {
            new EffectTarget(
                firstBacking,
                bounds,
                EffectiveScale.At(1),
                firstDeviceBounds,
                firstOffset),
            new EffectTarget(
                secondBacking,
                bounds,
                EffectiveScale.At(1),
                secondDeviceBounds,
                secondOffset),
        };

        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame,
            deviceGridOffset: ambientOffset);

        Assert.Multiple(() =>
        {
            Assert.That(context.DeviceGridOffset, Is.EqualTo(ambientOffset));
            Assert.That(context.Targets[0].DeviceGridOffset, Is.EqualTo(firstOffset));
            Assert.That(context.Targets[1].DeviceGridOffset, Is.EqualTo(secondOffset));
        });
    }

    [Test]
    public void CustomFilterContext_WrapsReplacementOnTheSourceDeviceGrid()
    {
        var bounds = new Rect(10, 12, 8, 6);
        var gridOffset = new Vector(0.25f, 0.75f);
        PixelRect deviceBounds = PixelRect.FromRect(bounds.Translate(gridOffset), 1);
        using RenderTarget sourceBacking = RenderTarget.CreateNull(
            deviceBounds.Width,
            deviceBounds.Height);
        using var source = new EffectTarget(
            sourceBacking,
            bounds,
            EffectiveScale.At(1),
            deviceBounds,
            gridOffset);
        using var targets = new EffectTargets { source.Clone() };
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame);
        using RenderTarget replacementBacking = RenderTarget.CreateNull(
            deviceBounds.Width,
            deviceBounds.Height);

        using EffectTarget replacement = context.CreateReplacement(
            source,
            replacementBacking);

        Assert.Multiple(() =>
        {
            Assert.That(replacement.DeviceGridOffset, Is.EqualTo(gridOffset));
            Assert.That(replacement.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(replacement.RasterBounds, Is.EqualTo(source.RasterBounds));
            Assert.That(replacement.Bounds, Is.EqualTo(source.Bounds));
        });
    }

    private static PixelRect MeasureAlphaBounds(Bitmap bitmap)
    {
        ReadOnlySpan<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        int left = bitmap.Width;
        int top = bitmap.Height;
        int right = 0;
        int bottom = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                int offset = ((y * bitmap.Width) + x) * 4;
                if ((float)BitConverter.UInt16BitsToHalf(pixels[offset + 3]) <= 0)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }
}
