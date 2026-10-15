using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;
using static Beutl.PublicApiContractTests.ContractTestSupport;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class RasterFootprintContractTests
{
    [Test]
    public void EffectItemEffectTarget_ExposesImmutableDeviceAndTranslatedRasterFootprints()
    {
        const float density = 2;
        var bounds = new Rect(10.25f, 20.25f, 8, 6);
        var translation = new Vector(3.25f, -1.5f);
        TargetFootprints? observed = null;
        using FilterEffect.Resource resource = new CustomEffectProbe(execution =>
        {
            using EffectTarget target = execution.CreateTarget(bounds);
            PixelRect allocation = target.DeviceBounds;
            Rect initialRasterBounds = target.RasterBounds;

            target.Bounds = target.Bounds.Translate(translation);

            observed = new TargetFootprints(
                allocation,
                new PixelSize(target.RenderTarget!.Width, target.RenderTarget.Height),
                initialRasterBounds,
                target.DeviceBounds,
                target.RasterBounds,
                target.Bounds);
        }).ToResource(CompositionContext.Default);
        using FilterEffectRenderNode node = CreateScene(resource, new Rect(0, 0, 40, 40));
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            OutputScale = density,
            MaxWorkingScale = density,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        using (renderer.Rasterize())
        {
        }

        Assert.That(observed, Is.Not.Null, "the custom effect callback has to have executed");
        TargetFootprints footprints = observed!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(footprints.Allocation.Position, Is.EqualTo(PixelRect.FromRect(bounds, density).Position));
            Assert.That(footprints.Allocation.Size, Is.EqualTo(footprints.BackingSize));
            Assert.That(footprints.TranslatedDeviceBounds, Is.EqualTo(footprints.Allocation));
            Assert.That(
                footprints.TranslatedRasterBounds,
                Is.EqualTo(footprints.InitialRasterBounds.Translate(translation)));
            Assert.That(footprints.TranslatedRasterBounds.Size, Is.EqualTo(footprints.InitialRasterBounds.Size));
            Assert.That(footprints.InitialRasterBounds.Position, Is.EqualTo(bounds.Position));
            Assert.That(footprints.TranslatedBounds.Size, Is.EqualTo(bounds.Size));
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
            Assert.That(shaderType.GetProperty("Effect"), Is.Null,
                "the owning shader must not expose its disposable runtime effect");
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

    private readonly record struct TargetFootprints(
        PixelRect Allocation,
        PixelSize BackingSize,
        Rect InitialRasterBounds,
        PixelRect TranslatedDeviceBounds,
        Rect TranslatedRasterBounds,
        Rect TranslatedBounds);

    // A plugin author's effect that hands its custom-effect context to the test.
    [SuppressResourceClassGeneration]
    private sealed partial class CustomEffectProbe(Action<CustomFilterEffectContext> run) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
            => context.CustomEffect(
                new ProbeAction(run),
                static (probe, execution) => probe.Run(execution),
                static (_, value) => value);

        public override Resource ToResource(CompositionContext context)
        {
            var created = new Resource();
            bool versionBumped = false;
            created.Reconcile(this, context, ref versionBumped);
            return created;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }

    private sealed class ProbeAction(Action<CustomFilterEffectContext> run) : IEquatable<ProbeAction>
    {
        public void Run(CustomFilterEffectContext context) => run(context);

        public bool Equals(ProbeAction? other) => ReferenceEquals(this, other);
    }
}
