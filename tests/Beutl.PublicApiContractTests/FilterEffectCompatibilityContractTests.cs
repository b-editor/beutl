using System.Buffers.Binary;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class FilterEffectCompatibilityContractTests
{
    private static readonly Rect s_bounds = new(3, 5, 12, 8);

    [Test]
    public void LegacyExecutionContexts_RequireAndExposeExplicitRequestClassification()
    {
        using var targets = new EffectTargets();
        using var builder = new SKImageFilterBuilder();
        using var activator = new FilterEffectActivator(
            targets,
            builder,
            RenderIntent.Preview,
            RenderRequestPurpose.HitTest);

        Type[] expectedParameterTypes =
        [
            typeof(EffectTargets),
            typeof(SKImageFilterBuilder),
            typeof(RenderIntent),
            typeof(RenderRequestPurpose),
            typeof(float),
            typeof(float),
            typeof(float),
        ];
        System.Reflection.ParameterInfo[] constructorParameters = typeof(FilterEffectActivator)
            .GetConstructors()
            .Single()
            .GetParameters();
        Type[] actualParameterTypes = constructorParameters
            .Select(static parameter => parameter.ParameterType)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(activator.Intent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(activator.Purpose, Is.EqualTo(RenderRequestPurpose.HitTest));
            Assert.That(actualParameterTypes, Is.EqualTo(expectedParameterTypes),
                "the former scale-only compatibility constructor must not remain public");
            Assert.That(constructorParameters[2].IsOptional, Is.False);
            Assert.That(constructorParameters[3].IsOptional, Is.False);
            Assert.That(constructorParameters.Skip(4).All(static parameter => parameter.IsOptional), Is.True);
            Assert.That(typeof(FilterEffectActivator).GetProperty(nameof(FilterEffectActivator.Intent))!.CanWrite,
                Is.False);
            Assert.That(typeof(FilterEffectActivator).GetProperty(nameof(FilterEffectActivator.Purpose))!.CanWrite,
                Is.False);
            Assert.That(typeof(CustomFilterEffectContext).GetProperty(nameof(CustomFilterEffectContext.Intent))!.CanWrite,
                Is.False);
            Assert.That(typeof(CustomFilterEffectContext).GetProperty(nameof(CustomFilterEffectContext.Purpose))!.CanWrite,
                Is.False);
        });
    }

    [Test]
    public void ExistingApplyToEffect_RetainsLegacyMembersBoundsAndDeferredExecution()
    {
        var executionOrder = new List<string>();
        var effect = new LegacyPluginEffect(executionOrder, "single");
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds, outputScale: 2, workingScale: 1.5f);

        effect.ApplyTo(context, resource);
        bool hasWorkingScale = context.TryGetWorkingScale(out float workingScale);

        Assert.Multiple(() =>
        {
            Assert.That(context.OriginalBounds, Is.EqualTo(s_bounds));
            Assert.That(context.Bounds, Is.EqualTo(s_bounds));
            Assert.That(context.OutputScale, Is.EqualTo(2));
            Assert.That(context.WorkingScale, Is.EqualTo(1.5f));
            Assert.That(hasWorkingScale, Is.True);
            Assert.That(workingScale, Is.EqualTo(1.5f));
            Assert.That(context.CountItems(), Is.EqualTo(5));
            Assert.That(executionOrder, Is.Empty,
                "ApplyTo must record legacy custom work rather than execute it.");
        });
    }

    [Test]
    public void ExistingApplyToEffect_RendersUnchangedAcrossGroupBoundariesInAuthoredOrder()
    {
        var executionOrder = new List<string>();
        var group = new FilterEffectGroup();
        group.Children.Add(new LegacyPluginEffect(executionOrder, "first"));
        group.Children.Add(new LegacyPluginEffect(executionOrder, "second"));

        using RenderNode unfiltered = new SolidSourceNode(s_bounds, Colors.CornflowerBlue);
        using RenderNode filtered = CreateEffectNode(group, new SolidSourceNode(s_bounds, Colors.CornflowerBlue));
        using RenderNodeRasterization baseline = Rasterize(unfiltered);

        Assert.That(executionOrder, Is.Empty,
            "Constructing and recording the legacy group must not invoke CustomEffect callbacks.");

        using RenderNodeRasterization actual = Rasterize(filtered);

        Assert.Multiple(() =>
        {
            Assert.That(actual.Bounds, Is.EqualTo(baseline.Bounds));
            Assert.That(actual.IsEmpty, Is.False);
            Assert.That(actual.Bitmap, Is.Not.Null);
            Assert.That(baseline.Bitmap, Is.Not.Null);
            Assert.That(executionOrder, Is.EqualTo(new[]
            {
                "first:after-color",
                "first:after-skia-transform",
                "second:after-color",
                "second:after-skia-transform",
            }));
            AssertBitmapsEqual(baseline.Bitmap!, actual.Bitmap!);
        });
    }

    [Test]
    public void WorkingScaleHook_ReusesBaseIsolationForMixedSymbolicFullInput()
    {
        var targetDomain = new Rect(10, 20, 48, 32);
        var sourceBounds = new Rect(14, 24, 12, 8);
        bool? hasWorkingScale = null;
        float probedWorkingScale = float.NaN;
        InvalidOperationException? getterFailure = null;
        var effect = new WorkingScaleProbeEffect(
            context =>
            {
                hasWorkingScale = context.TryGetWorkingScale(out probedWorkingScale);
                try
                {
                    _ = context.WorkingScale;
                }
                catch (InvalidOperationException ex)
                {
                    getterFailure = ex;
                }
            },
            static context => context.Brightness(1));
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var root = new SymbolicFullFilterInputNode(
            new BoundsDependentWorkingScaleFilterNode(resource),
            sourceBounds);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = targetDomain,
                UseRenderCache = false,
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.HasTargetEffects, Is.True);
            Assert.That(measurement.OutputBounds, Is.EqualTo(targetDomain),
                "the symbolic Full input must resolve against the owning target after base isolation");
            Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)),
                "the protected working-scale hook must be reevaluated through the base lowering path");
            Assert.That(hasWorkingScale, Is.False,
                "ApplyTo must not observe a provisional scale for a symbolic owning-target domain");
            Assert.That(probedWorkingScale, Is.Zero);
            Assert.That(getterFailure, Is.Not.Null);
            Assert.That(getterFailure!.Message, Does.Contain("unavailable"));
        });
    }

    [Test]
    public void WorkingScaleAvailability_IsFalseForConcreteMultiInputTypedLowering()
    {
        bool? hasWorkingScale = null;
        float probedWorkingScale = float.NaN;
        InvalidOperationException? getterFailure = null;
        var effect = new WorkingScaleProbeEffect(
            context =>
            {
                hasWorkingScale = context.TryGetWorkingScale(out probedWorkingScale);
                try
                {
                    _ = context.WorkingScale;
                }
                catch (InvalidOperationException ex)
                {
                    getterFailure = ex;
                }
            },
            static context => context.Shader(ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return color; }")));
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var root = new ConcreteMultiFilterInputNode(
            new BranchSensitiveWorkingScaleFilterNode(resource));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions { UseRenderCache = false });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(measurement.HasFragments, Is.True);
            Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(hasWorkingScale, Is.False,
                "one aggregate hint is not a final density when typed lowering keeps independent branches");
            Assert.That(probedWorkingScale, Is.Zero);
            Assert.That(getterFailure, Is.Not.Null);
            Assert.That(getterFailure!.Message, Does.Contain("different branches"));
        });
    }

    [Test]
    public void WorkingScaleAvailability_VectorHook_NormalizesCurrentPixelAtOutputScale()
    {
        bool? hasWorkingScale = null;
        float probedWorkingScale = float.NaN;
        float getterWorkingScale = float.NaN;
        var effect = new WorkingScaleProbeEffect(
            context =>
            {
                hasWorkingScale = context.TryGetWorkingScale(out probedWorkingScale);
                getterWorkingScale = context.WorkingScale;
            },
            static context => context.Shader(ShaderDescription.CurrentPixel(
                "half4 apply(half4 color) { return color; }")));
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var root = new ConcreteSingleFilterInputNode(
            new VectorWorkingScaleFilterNode(resource));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                OutputScale = 2,
                UseRenderCache = false,
            });

        RenderNodeMeasurement measurement = renderer.Measure();

        Assert.Multiple(() =>
        {
            Assert.That(hasWorkingScale, Is.True);
            Assert.That(probedWorkingScale, Is.EqualTo(2));
            Assert.That(getterWorkingScale, Is.EqualTo(2));
            Assert.That(measurement.EffectiveScale, Is.EqualTo(EffectiveScale.At(2)),
                "a Vector hook must not leave the first CurrentPixel shader unbounded after exposing w = 2");
        });
    }

    private static RenderNode CreateEffectNode(FilterEffect effect, RenderNode child)
    {
        FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        var node = resource.CreateRenderNode();
        node.AddChild(child);
        return new OwnedEffectNode(node, resource);
    }

    private static RenderNodeRasterization Rasterize(RenderNode node)
    {
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                OutputScale = 2,
                UseRenderCache = false,
                TargetFactory = new CpuTargetFactory(),
            });
        return renderer.Rasterize();
    }

    private static void AssertBitmapsEqual(Bitmap expected, Bitmap actual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(actual.Width, Is.EqualTo(expected.Width));
            Assert.That(actual.Height, Is.EqualTo(expected.Height));
            Assert.That(actual.ColorType, Is.EqualTo(expected.ColorType));
            Assert.That(actual.AlphaType, Is.EqualTo(expected.AlphaType));
        });

        ReadOnlySpan<byte> expectedPixels = expected.GetPixelSpan();
        ReadOnlySpan<byte> actualPixels = actual.GetPixelSpan();
        float maximumChannelError = 0;
        float maximumAlphaError = 0;
        for (int offset = 0; offset < expectedPixels.Length; offset += sizeof(ushort))
        {
            float expectedValue = (float)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(expectedPixels[offset..]));
            float actualValue = (float)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(actualPixels[offset..]));
            float error = MathF.Abs(expectedValue - actualValue);
            maximumChannelError = MathF.Max(maximumChannelError, error);
            if ((offset / sizeof(ushort)) % 4 == 3)
                maximumAlphaError = MathF.Max(maximumAlphaError, error);
        }

        Assert.That(
            maximumChannelError,
            Is.LessThanOrEqualTo(0.0025f),
            "Identity Skia filters may round RGBA16F channels while crossing legacy custom-effect buffers, "
            + "but must remain within a strict sub-visual-error bound.");
        Assert.That(maximumAlphaError, Is.Zero,
            "Identity legacy operations must preserve premultiplied alpha exactly.");
    }

    [SuppressResourceClassGeneration]
    private sealed partial class LegacyPluginEffect(List<string> executionOrder, string prefix) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            // This is intentionally an old-style ApplyTo implementation. It does not use any
            // feature-004 Shader/Geometry API and therefore exercises source compatibility.
            context.Brightness(1);
            context.CustomEffect(
                new LegacyMarker(executionOrder, $"{prefix}:after-color"),
                static (marker, callback) =>
                {
                    Assert.That(callback.Targets, Is.Not.Empty);
                    marker.Order.Add(marker.Name);
                },
                static (_, bounds) => bounds);
            context.Blur(Size.Empty);
            context.Transform(Matrix.Identity, BitmapInterpolationMode.Default);
            context.CustomEffect(
                new LegacyMarker(executionOrder, $"{prefix}:after-skia-transform"),
                static (marker, callback) =>
                {
                    Assert.That(callback.Targets, Is.Not.Empty);
                    marker.Order.Add(marker.Name);
                },
                static (_, bounds) => bounds);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }

    private sealed record LegacyMarker(List<string> Order, string Name);

    [SuppressResourceClassGeneration]
    private sealed partial class WorkingScaleProbeEffect(
        Action<FilterEffectContext> observe,
        Action<FilterEffectContext> record) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            observe(context);
            record(context);
        }

        public override Resource ToResource(CompositionContext context)
        {
            var resource = new Resource();
            bool updateOnly = false;
            resource.Update(this, context, ref updateOnly);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }

    private sealed class BoundsDependentWorkingScaleFilterNode(FilterEffect.Resource resource)
        : FilterEffectRenderNode(resource)
    {
        private static readonly RenderScaleContract s_scale = RenderScaleContract.Custom(
            static metadata => metadata.OutputBounds.Width >= 40 ? 2 : 0.5f,
            typeof(BoundsDependentWorkingScaleFilterNode));

        protected override RenderScaleContract? GetWorkingScaleContract() => s_scale;
    }

    private sealed class BranchSensitiveWorkingScaleFilterNode(FilterEffect.Resource resource)
        : FilterEffectRenderNode(resource)
    {
        private static readonly RenderScaleContract s_scale = RenderScaleContract.Custom(
            static metadata => metadata.InputSupplies.Count == 1
                ? metadata.InputSupplies[0].IsUnbounded
                    ? metadata.OutputScale
                    : metadata.InputSupplies[0].Value
                : 4,
            typeof(BranchSensitiveWorkingScaleFilterNode));

        protected override RenderScaleContract? GetWorkingScaleContract() => s_scale;
    }

    private sealed class VectorWorkingScaleFilterNode(FilterEffect.Resource resource)
        : FilterEffectRenderNode(resource)
    {
        protected override RenderScaleContract? GetWorkingScaleContract() => RenderScaleContract.Vector;
    }

    private sealed class SymbolicFullFilterInputNode(
        FilterEffectRenderNode filter,
        Rect sourceBounds) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle concrete = context.OpaqueSource(CreateMetadataSource(
                sourceBounds,
                RenderScaleContract.Custom(static _ => 4, "concrete-at-four"),
                "concrete"));
            RenderFragmentHandle scopedSource = context.OpaqueSource(CreateMetadataSource(
                sourceBounds,
                RenderScaleContract.Vector,
                "scoped"));
            RenderFragmentHandle symbolic = context.TargetLayerScope(
                [scopedSource],
                TargetRegion.Full);

            context.PublishRange(context.RecordNode(filter, [concrete, symbolic]));
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                filter.Dispose();

            base.OnDispose(disposing);
        }
    }

    private sealed class ConcreteMultiFilterInputNode(
        FilterEffectRenderNode filter) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle first = context.OpaqueSource(CreateMetadataSource(
                s_bounds,
                RenderScaleContract.Custom(static _ => 1, "concrete-at-one"),
                "concrete-at-one"));
            RenderFragmentHandle second = context.OpaqueSource(CreateMetadataSource(
                s_bounds.Translate(new Point(20, 0)),
                RenderScaleContract.Custom(static _ => 2, "concrete-at-two"),
                "concrete-at-two"));

            context.PublishRange(context.RecordNode(filter, [first, second]));
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                filter.Dispose();

            base.OnDispose(disposing);
        }
    }

    private sealed class ConcreteSingleFilterInputNode(
        FilterEffectRenderNode filter) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle source = context.OpaqueSource(CreateMetadataSource(
                s_bounds,
                RenderScaleContract.Custom(static _ => 1, "single-at-one"),
                "single-at-one"));
            context.PublishRange(context.RecordNode(filter, [source]));
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                filter.Dispose();

            base.OnDispose(disposing);
        }
    }

    private static OpaqueRenderDescription CreateMetadataSource(
        Rect bounds,
        RenderScaleContract scale,
        object structuralKey)
    {
        return OpaqueRenderDescription.Create(
            static _ => throw new AssertionException("Measure must not execute opaque callbacks."),
            RenderOperationBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            scale,
            structuralKey);
    }

    private sealed class SolidSourceNode(Rect bounds, Color color) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription description = OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(color));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: typeof(SolidSourceNode),
                runtimeIdentity: new RenderRuntimeIdentity((bounds, color)));
            context.Publish(context.OpaqueSource(description));
        }
    }

    private sealed class OwnedEffectNode(FilterEffectRenderNode node, FilterEffect.Resource resource) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            IReadOnlyList<RenderFragmentHandle> outputs = context.RecordSubtree(node);
            context.PublishRange(outputs);
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                node.Dispose();
                resource.Dispose();
            }

            base.OnDispose(disposing);
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize) => new CpuRenderTarget(deviceSize);
    }

    private sealed class CpuRenderTarget : RenderTarget
    {
        private static readonly SKColorSpace s_colorSpace = SKColorSpace.CreateSrgbLinear();

        public CpuRenderTarget(PixelSize size)
            : base(CreateSurface(size), size.Width, size.Height)
        {
        }

        private static SKSurface CreateSurface(PixelSize size)
        {
            return SKSurface.Create(new SKImageInfo(
                       size.Width,
                       size.Height,
                       SKColorType.RgbaF16,
                       SKAlphaType.Premul,
                       s_colorSpace))
                   ?? throw new InvalidOperationException("Could not create a CPU contract-test surface.");
        }
    }
}
