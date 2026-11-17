using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RasterFootprintContractTests
{
    [Test]
    public void EffectItemEffectTarget_ExposesImmutableDeviceAndTranslatedRasterFootprints()
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
        PixelRect allocation = target.Placement.DeviceBounds;
        Rect initialRasterBounds = target.Placement.RasterBounds;
        var translation = new Vector(3.25f, -1.5f);

        target.Bounds = target.Bounds.Translate(translation);

        Assert.Multiple(() =>
        {
            Assert.That(allocation.Position, Is.EqualTo(canonical.Position));
            Assert.That(allocation.Size,
                Is.EqualTo(new PixelSize(renderTarget.Width, renderTarget.Height)));
            Assert.That(target.Placement.DeviceBounds, Is.EqualTo(allocation));
            Assert.That(target.Placement.RasterBounds, Is.EqualTo(initialRasterBounds.Translate(translation)));
            Assert.That(target.Placement.ToLogical(allocation.Position), Is.EqualTo(initialRasterBounds.Position + translation));
            Assert.That(target.Placement.RasterBounds.Size, Is.EqualTo(initialRasterBounds.Size));
            Assert.That(initialRasterBounds.Position, Is.EqualTo(bounds.Position));
            Assert.That(target.Bounds.Size, Is.EqualTo(bounds.Size));
        });
    }

    [Test]
    public void CustomEffectItemBufferSize_RemainsLocalToLogicalDimensions()
    {
        const float density = 2;
        var bounds = new Rect(10.25f, 20.25f, 8, 6);

        PixelRect deviceBounds = CustomFilterEffectContext.DeviceBufferBounds(bounds, density);

        Assert.Multiple(() =>
        {
            Assert.That(deviceBounds, Is.EqualTo(PixelRect.FromRect(bounds, density)));
            Assert.That(deviceBounds.Size, Is.EqualTo(new PixelSize(17, 13)));
            Assert.That(CustomFilterEffectContext.DeviceBufferSize(bounds, density),
                Is.EqualTo((16, 12)));
        });
    }

    [Test]
    public void EffectItemCustomShaderApi_SeparatesAllocationMappingAndRendering()
    {
        Type contextType = typeof(CustomFilterEffectContext);
        Type shaderType = typeof(SKSLShader);
        Type shaderBuilderType = typeof(SKSLShaderBuilder);
        var useMappedInputShader = contextType.GetMethods()
            .Single(method => method.Name == nameof(CustomFilterEffectContext.UseMappedInputShader));
        Type stateType = useMappedInputShader.GetGenericArguments().Single();
        Type[] mappedInputParameters = useMappedInputShader.GetParameters()
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                contextType.GetMethod(
                    nameof(CustomFilterEffectContext.ResolveTargetDensity),
                    [typeof(Rect)])?.ReturnType,
                Is.EqualTo(typeof(float)));
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
                useMappedInputShader.IsGenericMethodDefinition,
                Is.True);
            Assert.That(
                useMappedInputShader.ReturnType,
                Is.EqualTo(typeof(bool)),
                "The mapped-input readback reports whether the callback ran so a degraded preview keeps its source target.");
            Assert.That(
                mappedInputParameters,
                Is.EqualTo(new[]
                {
                    typeof(EffectTarget),
                    typeof(EffectTarget),
                    stateType,
                    typeof(Action<,>).MakeGenericType(stateType, typeof(SKShader)),
                    typeof(SKShaderTileMode),
                    typeof(SKShaderTileMode),
                }));
            Assert.That(
                shaderType.GetMethod(
                    nameof(SKSLShader.RenderToTarget),
                    [typeof(CustomFilterEffectContext), shaderBuilderType, typeof(EffectTarget)]),
                Is.Not.Null);
            Assert.That(
                shaderType.GetMethod(nameof(SKSLShader.CreateBuilder), Type.EmptyTypes)?.ReturnType,
                Is.EqualTo(shaderBuilderType));
            Assert.That(
                shaderBuilderType.GetProperty(nameof(SKSLShaderBuilder.Uniforms))?.PropertyType,
                Is.EqualTo(typeof(SKRuntimeEffectUniforms)));
            Assert.That(
                shaderBuilderType.GetProperty(nameof(SKSLShaderBuilder.Children))?.PropertyType,
                Is.EqualTo(typeof(SKRuntimeEffectChildren)));
            Assert.That(
                shaderBuilderType.GetMethod(nameof(SKSLShaderBuilder.Build), Type.EmptyTypes)?.ReturnType,
                Is.EqualTo(typeof(SKShader)));
        });
    }

    [Test]
    public void PlacementMapsLocalBoundsAndCompositionPixelsOnTheSameGrid()
    {
        var bounds = new Rect(-3.25f, 5.5f, 4, 3);
        var offset = new Vector(0.75f, -0.25f);
        var placement = new RenderPlacement(bounds, EffectiveScale.At(2), new PixelRect(-6, 9, 12, 10), offset);

        Assert.Multiple(() =>
        {
            Assert.That(placement.ToLogical(placement.DeviceBounds.Position), Is.EqualTo(placement.LogicalOrigin));
            Assert.That(placement.ToLogical(new PixelPoint(-4, 11)), Is.EqualTo(new Point(-2.75f, 5.75f)));
            Assert.That(placement.ToDevice(bounds), Is.EqualTo(new PixelRect(-5, 10, 8, 7)));
            Assert.That(placement.ToDevice(placement.RasterBounds), Is.EqualTo(placement.DeviceBounds));
        });
    }

    [TestCase(typeof(EffectTarget), "Placement")]
    [TestCase(typeof(RenderExecutionInput), "Placement")]
    [TestCase(typeof(RenderCallbackCanvas), "Placement")]
    [TestCase(typeof(MaterializedInputDescription), "Placement")]
    [TestCase(typeof(OpaqueRenderOutput), "Placement")]
    [TestCase(typeof(ShaderExecutionContext), "OutputPlacement")]
    [TestCase(typeof(GeometrySession), "OutputPlacement")]
    [TestCase(typeof(OpaqueRenderSession), "OutputPlacement")]
    public void RasterFacadesExposeTheSharedPlacement(Type facade, string name)
    {
        Assert.That(facade.GetProperty(name)?.PropertyType, Is.EqualTo(typeof(RenderPlacement)));
    }
}
