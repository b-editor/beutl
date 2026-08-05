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

            Assert.Multiple(() =>
            {
                Assert.That(facade.Density, Is.EqualTo(1));
                Assert.That(facade.DeviceGridOffset, Is.EqualTo(translation));
                Assert.That(facade.DeviceBounds, Is.EqualTo(new PixelRect(49, 70, 151, 110)));
                Assert.That(facade.RasterBounds, Is.EqualTo(new Rect(-0.97410583f, 0, 151, 110)));
            });
        }

        token.Complete();
    }

    [TestCase(1.7f)]
    [TestCase(1.3333333f)]
    [TestCase(1.06f)]
    public void CallbackCanvas_AcceptsLargeCanonicalDeviceFootprints(float density)
    {
        var logicalBounds = new Rect(0, 0, 1920, 1080);
        PixelRect deviceBounds = PixelRect.FromRect(logicalBounds, density);
        Rect rasterBounds = deviceBounds.ToRect(density);
        var token = new RenderExecutionSessionToken();
        try
        {
            Assert.That(
                () => new RenderCallbackCanvas(
                    token,
                    density,
                    logicalBounds,
                    deviceBounds,
                    static () => throw new InvalidOperationException("The constructor must not open a canvas."),
                    CallbackCanvasCapability.Draw,
                    rasterBounds: rasterBounds),
                Throws.Nothing);
        }
        finally
        {
            token.Complete();
        }
    }

    [Test]
    public void DeviceBoundsValidation_RejectsOffByOneExtentAboveFloatPrecisionBoundary()
    {
        const int deviceExtent = 8_388_610;

        Assert.Multiple(() =>
        {
            Assert.That(
                DeviceBoundsValidation.MatchesExtent(deviceExtent, density: 1, deviceExtent),
                Is.True);
            Assert.That(
                DeviceBoundsValidation.MatchesExtent(deviceExtent + 1, density: 1, deviceExtent),
                Is.False,
                "An off-by-one backing extent must not be accepted when float ULPs exceed one pixel.");
        });
    }

    [TestCase(1.7f)]
    [TestCase(1.3333333f)]
    [TestCase(1.06f)]
    public void ExecutionInput_AcceptsLargeCanonicalDeviceFootprints(float density)
    {
        var logicalBounds = new Rect(0, 0, 1920, 1080);
        PixelRect deviceBounds = PixelRect.FromRect(logicalBounds, density);
        Rect rasterBounds = deviceBounds.ToRect(density);
        var token = new RenderExecutionSessionToken();
        try
        {
            Assert.That(
                () => new RenderExecutionInput(
                    token,
                    logicalBounds,
                    EffectiveScale.At(density),
                    deviceBounds,
                    rasterBounds,
                    draw: static (_, _) => { },
                    drawDeviceSpace: static (_, _) => { },
                    createShader: null,
                    createSnapshot: null,
                    readbackDeclared: false),
                Throws.Nothing);
        }
        finally
        {
            token.Complete();
        }
    }

    [Test]
    public void CallbackCanvas_RejectsAnOffByOneBackingExtent()
    {
        const float density = 1.7f;
        var logicalBounds = new Rect(0, 0, 1920, 1080);
        PixelRect canonical = PixelRect.FromRect(logicalBounds, density);
        Rect rasterBounds = canonical.ToRect(density);
        var mismatched = new PixelRect(
            canonical.X,
            canonical.Y,
            canonical.Width + 1,
            canonical.Height);
        var token = new RenderExecutionSessionToken();
        try
        {
            Assert.That(
                () => new RenderCallbackCanvas(
                    token,
                    density,
                    logicalBounds,
                    mismatched,
                    static () => throw new InvalidOperationException("The constructor must not open a canvas."),
                    CallbackCanvasCapability.Draw,
                    rasterBounds: rasterBounds),
                Throws.ArgumentException.With.Message.Contains("backing size"));
        }
        finally
        {
            token.Complete();
        }
    }

    [Test]
    public void RenderNodeRenderer_TargetScopeRendersAtScaleOnePointSeven()
    {
        const float density = 1.7f;
        var logicalBounds = new Rect(0, 0, 1920, 1080);
        using var root = new TransformRenderNode(Matrix.Identity, TransformOperator.Prepend);
        root.AddChild(new RectangleRenderNode(logicalBounds, Brushes.Resource.White, pen: null));
        using var target = new CpuRenderTarget(
            (int)Math.Ceiling(logicalBounds.Width * density),
            (int)Math.Ceiling(logicalBounds.Height * density));
        using var destination = new ImmediateCanvas(
            target,
            density,
            logicalSize: logicalBounds.Size);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = logicalBounds,
                    OutputScale = density,
                    MaxWorkingScale = density,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
            });

        Assert.That(() => renderer.Render(destination), Throws.Nothing);
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
        var deviceGridOffset = new Vector(0.25f, -0.125f);
        PixelRect deviceBounds = PixelRect.FromRect(bounds.Translate(deviceGridOffset), density);
        using RenderTarget renderTarget = RenderTarget.CreateNull(deviceBounds.Width, deviceBounds.Height);
        using var target = new EffectTarget(
            renderTarget,
            bounds,
            EffectiveScale.At(density),
            deviceBounds,
            deviceGridOffset);
        Rect initialRasterBounds = deviceBounds.ToRect(density).Translate(-deviceGridOffset);
        var translation = new Vector(3.25f, -1.5f);

        target.Bounds = target.Bounds.Translate(translation);
        using EffectTarget clone = target.Clone();
        using var targets = new EffectTargets { target.Clone() };
        using EffectTargets clonedTargets = targets.Clone();

        Assert.Multiple(() =>
        {
            Assert.That(target.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(target.DeviceGridOffset, Is.EqualTo(deviceGridOffset));
            Assert.That(target.RasterBounds, Is.EqualTo(initialRasterBounds.Translate(translation)));
            Assert.That(target.RasterBounds.Size, Is.EqualTo(initialRasterBounds.Size));
            Assert.That(clone.DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(clone.DeviceGridOffset, Is.EqualTo(deviceGridOffset));
            Assert.That(clone.Bounds, Is.EqualTo(target.Bounds));
            Assert.That(clone.RasterBounds, Is.EqualTo(target.RasterBounds));
            Assert.That(clonedTargets[0].DeviceBounds, Is.EqualTo(deviceBounds));
            Assert.That(clonedTargets[0].DeviceGridOffset, Is.EqualTo(deviceGridOffset));
            Assert.That(clonedTargets[0].RasterBounds, Is.EqualTo(target.RasterBounds));
        });
    }

    [Test]
    public void DeviceBufferSize_RemainsIndependentFromCanonicalDeviceOrigin()
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
                Is.EqualTo((16, 12)));
        });
    }

    [Test]
    public void ResolveTargetDensity_UsesLegacyLocalDimensions()
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
        (int width, int height) = CustomFilterEffectContext.DeviceBufferSize(
            requestedBounds,
            density);

        Assert.Multiple(() =>
        {
            Assert.That(density, Is.EqualTo(1));
            Assert.That(width, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
            Assert.That(height, Is.LessThanOrEqualTo(RenderScaleUtilities.MaxBufferDimension));
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

    [Test]
    public void CustomFilterContext_ScopesGpuBackedMappedInputShader()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var bounds = new Rect(0, 0, 8, 6);
            PixelRect deviceBounds = PixelRect.FromRect(bounds, 1);
            using RenderTarget sourceBacking = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height)
                ?? throw new AssertionException("Vulkan did not create the mapped-input source target.");
            using RenderTarget destinationBacking = RenderTarget.Create(deviceBounds.Width, deviceBounds.Height)
                ?? throw new AssertionException("Vulkan did not create the mapped-input destination target.");
            using var source = new EffectTarget(
                sourceBacking,
                bounds,
                EffectiveScale.At(1),
                deviceBounds);
            using var destination = new EffectTarget(
                destinationBacking,
                bounds,
                EffectiveScale.At(1),
                deviceBounds);
            using var targets = new EffectTargets { source.Clone() };
            var context = new CustomFilterEffectContext(
                targets,
                RenderIntent.Preview,
                RenderRequestPurpose.Frame);
            int[] callbackEntries = [0];

            bool rendered = context.UseMappedInputShader(
                source,
                destination,
                callbackEntries,
                static (entries, shader) =>
                {
                    entries[0]++;
                    using SKShader remapped = shader.WithLocalMatrix(SKMatrix.Identity);
                    Assert.That(remapped, Is.Not.Null);
                },
                SKShaderTileMode.Repeat,
                SKShaderTileMode.Mirror);

            Assert.Multiple(() =>
            {
                Assert.That(callbackEntries[0], Is.EqualTo(1));
                Assert.That(rendered, Is.True, "A successful readback must report that the callback ran.");
            });

            Assert.Multiple(() =>
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => context.UseMappedInputShader(
                    source,
                    destination,
                    0,
                    static (_, _) => { },
                    (SKShaderTileMode)(-1)));
                Assert.Throws<ArgumentOutOfRangeException>(() => context.UseMappedInputShader(
                    source,
                    destination,
                    0,
                    static (_, _) => { },
                    y: (SKShaderTileMode)(-1)));
            });
        });
    }

    [Test]
    public void SkslShaderBuilder_RejectsCrossOwnerAndDisposedOwnerUse()
    {
        const string source = "half4 main(float2 coord) { return half4(1); }";
        using SKSLShader first = SKSLShader.Create(source);
        using SKSLShader second = SKSLShader.Create(source);
        using SKSLShaderBuilder secondBuilder = second.CreateBuilder();
        using var targets = new EffectTargets();
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame);
        using var emptyTarget = new EffectTarget();

        Assert.That(
            () => first.RenderToTarget(context, secondBuilder, emptyTarget),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("builder"));

        using SKSLShader disposedOwner = SKSLShader.Create(source);
        using SKSLShaderBuilder disposedOwnerBuilder = disposedOwner.CreateBuilder();
        disposedOwner.Dispose();

        Assert.That(
            () => disposedOwnerBuilder.Build(),
            Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public void CustomFilterContext_ReplacementFootprintFailureNamesPublicArgument()
    {
        var bounds = new Rect(10, 12, 8, 6);
        PixelRect deviceBounds = PixelRect.FromRect(bounds, 1);
        using RenderTarget sourceBacking = RenderTarget.CreateNull(
            deviceBounds.Width,
            deviceBounds.Height);
        using var source = new EffectTarget(
            sourceBacking,
            bounds,
            EffectiveScale.At(1),
            deviceBounds);
        using var targets = new EffectTargets { source.Clone() };
        var context = new CustomFilterEffectContext(
            targets,
            RenderIntent.Preview,
            RenderRequestPurpose.Frame);
        using RenderTarget wrongSize = RenderTarget.CreateNull(
            deviceBounds.Width + 1,
            deviceBounds.Height);

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => context.CreateReplacement(source, wrongSize))!;

        Assert.Multiple(() =>
        {
            Assert.That(exception.ParamName, Is.EqualTo("renderTarget"));
            Assert.That(exception.Message, Does.Contain("footprint"));
            Assert.That(exception.Message, Does.Contain($"{deviceBounds.Width}x{deviceBounds.Height}"));
        });
    }

    [Test]
    public void MeasureAlphaBounds_IgnoresNonFiniteAndNonPositiveAlpha()
    {
        using var bitmap = new Bitmap(
            6,
            1,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        Span<ushort> pixels = bitmap.GetPixelSpan<ushort>();
        float[] alphaValues = [float.NegativeInfinity, -1, 0, float.NaN, float.PositiveInfinity, 0.5f];
        for (int x = 0; x < alphaValues.Length; x++)
        {
            pixels[(x * 4) + 3] = BitConverter.HalfToUInt16Bits((Half)alphaValues[x]);
        }

        Assert.That(MeasureAlphaBounds(bitmap), Is.EqualTo(new PixelRect(5, 0, 1, 1)));
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
                float alpha = (float)BitConverter.UInt16BitsToHalf(pixels[offset + 3]);
                if (!float.IsFinite(alpha) || alpha <= 0)
                    continue;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return new PixelRect(left, top, right - left, bottom - top);
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
