using System.Buffers.Binary;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shaders;
using Beutl.Media;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class FilterEffectCompatibilityContractTests
{
    private static readonly Rect s_bounds = new(3, 5, 12, 8);
    private static readonly ShaderDescription s_identityShader =
        ShaderDescription.CurrentPixel("half4 apply(half4 color) { return color; }");

    [Test]
    public void ExistingApplyToEffect_RetainsEffectItemMembersAndDeferredExecution()
    {
        var executionOrder = new List<string>();
        var effect = new EffectItemPluginEffect(executionOrder, "single");
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var context = new FilterEffectContext(s_bounds, outputScale: 2, workingScale: 1.5f);

        effect.ApplyTo(context, resource);
        bool hasWorkingScale = context.TryGetWorkingScale(out float workingScale);

        Assert.Multiple(() =>
        {
            Assert.That(context.OriginalBounds, Is.EqualTo(s_bounds));
            Assert.That(context.OutputScale, Is.EqualTo(2));
            Assert.That(context.WorkingScale, Is.EqualTo(1.5f));
            Assert.That(hasWorkingScale, Is.True);
            Assert.That(workingScale, Is.EqualTo(1.5f));
            Assert.That(executionOrder, Is.Empty,
                "ApplyTo must record effectItem custom work rather than execute it.");
        });
    }

    [Test]
    public void ExistingApplyToEffect_RendersUnchangedAcrossGroupBoundariesInAuthoredOrder()
    {
        var executionOrder = new List<string>();
        var group = new FilterEffectGroup();
        group.Children.Add(new EffectItemPluginEffect(executionOrder, "first"));
        group.Children.Add(new EffectItemPluginEffect(executionOrder, "second"));

        using RenderNode unfiltered = new SolidSourceNode(s_bounds, Colors.CornflowerBlue);
        using RenderNode filtered = CreateEffectNode(group, new SolidSourceNode(s_bounds, Colors.CornflowerBlue));
        using RenderNodeRasterization baseline = Rasterize(unfiltered);

        Assert.That(executionOrder, Is.Empty,
            "Constructing and recording the effectItem group must not invoke CustomEffect callbacks.");

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
            static context => context.Brightness(0.75f));
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var root = new FilterInputNode(
            new BoundsDependentWorkingScaleFilterNode(resource),
            context =>
            {
                RenderFragmentHandle concrete = context.OpaqueSource(CreateMetadataSource(
                    sourceBounds,
                    RenderScaleContract.Custom(static _ => 4)));
                RenderFragmentHandle scopedSource = context.OpaqueSource(CreateMetadataSource(
                    sourceBounds,
                    RenderScaleContract.Vector));
                RenderFragmentHandle symbolic = context.TargetLayerScope(
                    [scopedSource],
                    TargetRegion.Full);
                return [concrete, symbolic];
            });
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = targetDomain,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
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
            static context => context.Shader(s_identityShader));
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var root = new FilterInputNode(
            new BranchSensitiveWorkingScaleFilterNode(resource),
            static context =>
            {
                RenderFragmentHandle first = context.OpaqueSource(CreateMetadataSource(
                    s_bounds,
                    RenderScaleContract.Custom(static _ => 1)));
                RenderFragmentHandle second = context.OpaqueSource(CreateMetadataSource(
                    s_bounds.Translate(new Point(20, 0)),
                    RenderScaleContract.Custom(static _ => 2)));
                return [first, second];
            });
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            });

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
            static context => context.Shader(s_identityShader));
        using FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        using var root = new FilterInputNode(
            new VectorWorkingScaleFilterNode(resource),
            static context =>
            [
                context.OpaqueSource(CreateMetadataSource(
                    s_bounds,
                    RenderScaleContract.Custom(static _ => 1))),
            ]);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                OutputScale = 2,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
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
        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            OutputScale = 2,
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
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
            "Identity Skia filters may round RGBA16F channels while crossing effectItem custom-effect buffers, "
            + "but must remain within a strict sub-visual-error bound.");
        Assert.That(maximumAlphaError, Is.Zero,
            "Identity effectItem operations must preserve premultiplied alpha exactly.");
    }

    [SuppressResourceClassGeneration]
    private sealed partial class EffectItemPluginEffect(List<string> executionOrder, string prefix) : FilterEffect
    {
        public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
        {
            // Deliberately uses only the pre-Shader/Geometry ApplyTo surface, to pin source compatibility.
            context.Brightness(1);
            context.CustomEffect(
                new EffectItemMarker(executionOrder, $"{prefix}:after-color"),
                static (marker, callback) =>
                {
                    Assert.That(callback.Targets, Is.Not.Empty);
                    marker.Order.Add(marker.Name);
                },
                static (_, bounds) => bounds);
            context.Blur(Size.Empty);
            context.Transform(Matrix.Identity, BitmapInterpolationMode.Default);
            context.CustomEffect(
                new EffectItemMarker(executionOrder, $"{prefix}:after-skia-transform"),
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
            bool versionBumped = false;
            resource.Reconcile(this, context, ref versionBumped);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }

    private sealed record EffectItemMarker(List<string> Order, string Name);

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
            bool versionBumped = false;
            resource.Reconcile(this, context, ref versionBumped);
            return resource;
        }

        public new sealed class Resource : FilterEffect.Resource;
    }

    private sealed class BoundsDependentWorkingScaleFilterNode(FilterEffect.Resource resource)
        : FilterEffectRenderNode(resource)
    {
        private static readonly RenderScaleContract s_scale = RenderScaleContract.Custom(
            static metadata => metadata.OutputBounds.Width >= 40 ? 2 : 0.5f);

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
                : 4);

        protected override RenderScaleContract? GetWorkingScaleContract() => s_scale;
    }

    private sealed class VectorWorkingScaleFilterNode(FilterEffect.Resource resource)
        : FilterEffectRenderNode(resource)
    {
        protected override RenderScaleContract? GetWorkingScaleContract() => RenderScaleContract.Vector;
    }

    private sealed class FilterInputNode(
        FilterEffectRenderNode filter,
        Func<RenderNodeContext, RenderFragmentHandle[]> createInputs) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => context.PublishRange(context.RecordNode(filter, createInputs(context)));

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                filter.Dispose();

            base.OnDispose(disposing);
        }
    }

    private static OpaqueRenderDescription CreateMetadataSource(Rect bounds, RenderScaleContract scale)
    {
        return RenderDescriptionFactory.Opaque(
            static _ => throw new AssertionException("Measure must not execute opaque callbacks."),
            OpaqueRenderBoundsContract.Source(bounds),
            RenderHitTestContract.OutputBounds,
            RenderValueCardinality.Single,
            scale);
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
}
