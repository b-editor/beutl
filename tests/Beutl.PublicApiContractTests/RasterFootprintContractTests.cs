using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RasterFootprintContractTests
{
    [Test]
    public void LegacyEffectTarget_ExposesImmutableDeviceAndTranslatedRasterFootprints()
    {
        const float density = 2;
        var bounds = new Rect(10.25f, 20.25f, 8, 6);
        PixelRect canonical = PixelRect.FromRect(bounds, density);
        using RenderTarget renderTarget = RenderTarget.CreateNull(
            canonical.Width + 1,
            canonical.Height + 2);
        using var target = new EffectTarget(
            renderTarget,
            bounds,
            EffectiveScale.At(density));
        PixelRect allocation = target.DeviceBounds;
        Rect initialRasterBounds = target.RasterBounds;
        var translation = new Vector(3.25f, -1.5f);

        target.Bounds = target.Bounds.Translate(translation);

        Assert.Multiple(() =>
        {
            Assert.That(allocation.Position, Is.EqualTo(canonical.Position));
            Assert.That(allocation.Size,
                Is.EqualTo(new PixelSize(renderTarget.Width, renderTarget.Height)));
            Assert.That(target.DeviceBounds, Is.EqualTo(allocation));
            Assert.That(target.RasterBounds, Is.EqualTo(initialRasterBounds.Translate(translation)));
            Assert.That(target.RasterBounds.Size, Is.EqualTo(initialRasterBounds.Size));
            Assert.That(target.Bounds.Size, Is.EqualTo(bounds.Size));
        });
    }

    [Test]
    public void LegacyCustomEffectBufferHelpers_UseCanonicalCompositionDeviceBounds()
    {
        const float density = 2;
        var bounds = new Rect(10.25f, 20.25f, 8, 6);

        PixelRect deviceBounds = CustomFilterEffectContext.DeviceBufferBounds(bounds, density);

        Assert.Multiple(() =>
        {
            Assert.That(deviceBounds, Is.EqualTo(PixelRect.FromRect(bounds, density)));
            Assert.That(deviceBounds.Size, Is.EqualTo(new PixelSize(17, 13)));
            Assert.That(CustomFilterEffectContext.DeviceBufferSize(bounds, density),
                Is.EqualTo((deviceBounds.Width, deviceBounds.Height)));
        });
    }

    [Test]
    public void LegacyCustomShaderApi_SeparatesAllocationMappingAndRendering()
    {
        Type contextType = typeof(CustomFilterEffectContext);
        Type shaderType = typeof(SKSLShader);

        Assert.Multiple(() =>
        {
            Assert.That(
                contextType.GetMethod(
                    nameof(CustomFilterEffectContext.CreateTargetLike),
                    [typeof(EffectTarget)]),
                Is.Not.Null);
            Assert.That(
                contextType.GetMethod(
                    nameof(CustomFilterEffectContext.CreateReplacement),
                    [typeof(EffectTarget), typeof(RenderTarget)]),
                Is.Not.Null);
            Assert.That(
                contextType.GetMethod(
                    nameof(CustomFilterEffectContext.CreateMappedInputShader),
                    [typeof(EffectTarget), typeof(EffectTarget), typeof(SKShader)]),
                Is.Not.Null);
            Assert.That(
                contextType.GetMethod(
                    nameof(CustomFilterEffectContext.UseMappedInputShader),
                    [
                        typeof(EffectTarget),
                        typeof(EffectTarget),
                        typeof(Action<SKShader>),
                        typeof(SKShaderTileMode),
                        typeof(SKShaderTileMode),
                    ]),
                Is.Not.Null);
            Assert.That(
                shaderType.GetMethod(
                    nameof(SKSLShader.RenderToTarget),
                    [typeof(CustomFilterEffectContext), typeof(SKRuntimeShaderBuilder), typeof(EffectTarget)]),
                Is.Not.Null);
            Assert.That(shaderType.GetMethod("ApplyToNewTarget"), Is.Null,
                "the allocation-owning compatibility overload must not remain public");
        });
    }

    [Test]
    public void GridAwareRasterFacades_ExposeTheCompositionDeviceTranslation()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                typeof(EffectTarget).GetProperty(nameof(EffectTarget.DeviceGridOffset)),
                Is.Not.Null);
            Assert.That(
                typeof(CustomFilterEffectContext).GetProperty(
                    nameof(CustomFilterEffectContext.DeviceGridOffset)),
                Is.Not.Null);
            Assert.That(
                typeof(RenderExecutionInput).GetProperty(
                    nameof(RenderExecutionInput.DeviceGridOffset)),
                Is.Not.Null);
            Assert.That(
                typeof(RenderCallbackCanvas).GetProperty(
                    nameof(RenderCallbackCanvas.DeviceGridOffset)),
                Is.Not.Null);
            Assert.That(
                typeof(ShaderExecutionContext).GetProperty(
                    nameof(ShaderExecutionContext.DeviceGridOffset)),
                Is.Not.Null);
        });
    }
}
