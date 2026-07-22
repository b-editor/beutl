using System.Reactive;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Planning;

[TestFixture]
public sealed class SymbolicOwningDomainTests
{
    private static readonly Rect s_rootDomain = new(0, 0, 100, 60);

    [Test]
    public void UnknownLegacy_UnderTranslation_ResolvesLocalDomainBeforeMappingToRoot()
    {
        var effect = new SymbolicDomainFilterEffect();
        using TransformRenderNode root = WrapInTranslation(
            CreateFilter(effect, new Rect(-5, 10, 10, 10)),
            10);

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        RenderFragmentReference legacy = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect);
        RenderFragmentReference transform = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.TargetScope);

        Assert.Multiple(() =>
        {
            Assert.That(legacy.BoundsRequirement, Is.EqualTo(RenderFragmentBoundsRequirement.OwningTargetDomain));
            Assert.That(legacy.RecordedBounds, Is.EqualTo(new Rect(-5, 10, 10, 10)));
            Assert.That(legacy.Bounds, Is.EqualTo(new Rect(-10, 0, 100, 60)));
            Assert.That(transform.Bounds, Is.EqualTo(s_rootDomain));
            Assert.That(compiled.Regions.GetMetadata(legacy).Bounds, Is.EqualTo(legacy.Bounds));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(s_rootDomain));
        });
    }

    [Test]
    public void UnknownLegacy_LargeResolvedDomain_RecomputesDownstreamScaleClamp()
    {
        var domain = new Rect(0, 0, 10_000, 100);
        var effect = new SymbolicDomainFilterEffect();
        using TransformRenderNode root = WrapInTranslation(
            CreateFilter(effect, new Rect(5, 6, 20, 12)),
            0);

        using CompiledRenderRequest compiled = Compile(root, domain, outputScale: 2);
        RenderFragmentReference legacy = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect);
        RenderFragmentReference transform = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.TargetScope);
        float expected = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(domain, 2);

        Assert.Multiple(() =>
        {
            Assert.That(legacy.RecordedEffectiveScale, Is.EqualTo(EffectiveScale.At(2)));
            Assert.That(legacy.EffectiveScale.Value, Is.EqualTo(expected).Within(1e-6f));
            Assert.That(transform.EffectiveScale.Value, Is.EqualTo(expected).Within(1e-6f));
            Assert.That(compiled.Regions.GetMetadata(transform).EffectiveScale,
                Is.EqualTo(transform.EffectiveScale));
        });
    }

    [Test]
    public void UnknownLegacy_NoOpUnderTranslation_PreservesPixelsFromLocalNegativeCoordinates()
    {
        var effect = new SymbolicDomainFilterEffect();
        using TransformRenderNode root = WrapInTranslation(
            CreateFilter(effect, new Rect(-5, 10, 10, 10)),
            10);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_rootDomain,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
            });
        using var target = new CpuRenderTarget(100, 60);
        using var canvas = new ImmediateCanvas(target);

        renderer.Render(canvas);
        using Bitmap bitmap = target.Snapshot();

        Assert.Multiple(() =>
        {
            Assert.That(effect.CallbackCount, Is.EqualTo(1));
            Assert.That(AlphaAt(bitmap, 6, 15), Is.GreaterThan(0.9f));
        });
    }

    [Test]
    public void UnknownLegacy_UnderIntersectClip_ResolvesToClippedLocalDomain()
    {
        var clip = new Rect(20, 5, 30, 40);
        var effect = new SymbolicDomainFilterEffect();
        using var root = new RectClipRenderNode(clip, ClipOperation.Intersect);
        root.AddChild(CreateFilter(effect, new Rect(25, 10, 5, 5)));

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        RenderFragmentReference legacy = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect);
        RenderFragmentReference clipped = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.TargetScope);

        Assert.Multiple(() =>
        {
            Assert.That(legacy.Bounds, Is.EqualTo(clip));
            Assert.That(clipped.Bounds, Is.EqualTo(clip));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(clip));
        });
    }

    [Test]
    public void UnknownLegacy_ExplicitTargetlessDomain_ResolvesDuringMetadata()
    {
        var domain = new Rect(-20, -10, 80, 50);
        var effect = new SymbolicDomainFilterEffect();
        using FilterEffectRenderNode root = CreateFilter(effect, new Rect(5, 6, 20, 12));

        using CompiledRenderRequest compiled = Compile(root, domain);
        RenderFragmentReference legacy = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect);

        Assert.Multiple(() =>
        {
            Assert.That(legacy.Bounds, Is.EqualTo(domain));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(domain));
            Assert.That(effect.CallbackCount, Is.Zero);
        });
    }

    [Test]
    public void UnknownLegacy_RealDestinationSuppliesOwningDomain()
    {
        var effect = new SymbolicDomainFilterEffect();
        using FilterEffectRenderNode root = CreateFilter(effect, new Rect(5, 6, 20, 12));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
            });
        using var target = new CpuRenderTarget(64, 48);
        using var canvas = new ImmediateCanvas(target);

        Assert.That(() => renderer.Render(canvas), Throws.Nothing);
        Assert.That(effect.CallbackCount, Is.EqualTo(1));
    }

    [Test]
    public void UnknownLegacy_WithoutOwningDomain_FailsBeforeRuntimeCallback()
    {
        var effect = new SymbolicDomainFilterEffect();
        using FilterEffectRenderNode root = CreateFilter(effect, new Rect(5, 6, 20, 12));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions { UseRenderCache = false });

        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() => renderer.Measure());

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("transformBounds").And.Contain("owning target domain"));
            Assert.That(effect.CallbackCount, Is.Zero);
        });
    }

    [Test]
    public void UnknownLegacy_InsideFiniteLayer_UsesLayerDomainWithoutRootDomain()
    {
        var domain = new Rect(-20, 5, 40, 30);
        var effect = new SymbolicDomainFilterEffect();
        using var root = new LayerRenderNode(domain);
        root.AddChild(CreateFilter(effect, new Rect(-10, 10, 5, 5)));

        using CompiledRenderRequest compiled = Compile(root, targetDomain: null);
        RenderFragmentReference legacy = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect);

        Assert.Multiple(() =>
        {
            Assert.That(legacy.Bounds, Is.EqualTo(domain));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(domain));
            Assert.That(effect.CallbackCount, Is.Zero);
        });
    }

    [Test]
    public void FullTargetCommand_FilterUsesOwningDomainLayerAndRecomputesLegacyBounds()
    {
        var effect = new FiniteLegacyFilterEffect();
        using var root = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        root.AddChild(new ClearRenderNode(Colors.White));

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        RenderFragmentReference layer = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.Layer);
        RenderFragmentReference legacy = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect);
        var layerPayload = (LayerRenderFragmentPayload)layer.Payload!;

        Assert.Multiple(() =>
        {
            Assert.That(effect.ObservedInputBounds.IsInvalid, Is.True,
                "A full target domain must remain symbolic while the filter records.");
            Assert.That(layerPayload.Domain, Is.Null,
                "The internal Layer must resolve from its owning target instead of freezing a root placeholder.");
            Assert.That(layer.BoundsRequirement,
                Is.EqualTo(RenderFragmentBoundsRequirement.OwningTargetDomain));
            Assert.That(layer.Bounds, Is.EqualTo(s_rootDomain));
            Assert.That(legacy.Bounds, Is.EqualTo(s_rootDomain.Inflate(new Thickness(2))));
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(s_rootDomain));
        });
    }

    [Test]
    public void TargetIndependentTransform_SymbolicOwningDomain_DefersRelativeOriginUntilResolution()
    {
        using var symbolicRoot = new FilterEffectRenderNode(
            CreateTargetIndependentHalfScaleEffect().ToResource(CompositionContext.Default));
        symbolicRoot.AddChild(new ClearRenderNode(Colors.White));

        using CompiledRenderRequest compiled = Compile(symbolicRoot, s_rootDomain);
        RenderFragmentReference legacy = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect);
        Vector origin = RelativePoint.Center.ToPixels(s_rootDomain.Size) + s_rootDomain.Position;
        Matrix offset = Matrix.CreateTranslation(origin);
        Rect expectedBounds = s_rootDomain.TransformToAABB(
            (-offset) * Matrix.CreateScale(0.5f, 0.5f) * offset);

        using Bitmap symbolic = RenderToBitmap(symbolicRoot, s_rootDomain);
        using var finiteRoot = new FilterEffectRenderNode(
            CreateTargetIndependentHalfScaleEffect().ToResource(CompositionContext.Default));
        finiteRoot.AddChild(new RectangleRenderNode(s_rootDomain, Brushes.Resource.White, null));
        using Bitmap finite = RenderToBitmap(finiteRoot, s_rootDomain);

        Assert.Multiple(() =>
        {
            Assert.That(legacy.Bounds.IsInvalid, Is.False);
            Assert.That(legacy.Bounds, Is.EqualTo(expectedBounds));
            Assert.That(AlphaAt(symbolic, 50, 30), Is.GreaterThan(0.9f));
            foreach (PixelPoint point in new[]
                     {
                         new PixelPoint(50, 30),
                         new PixelPoint(30, 20),
                         new PixelPoint(70, 40),
                         new PixelPoint(10, 10),
                         new PixelPoint(90, 50),
                     })
            {
                Assert.That(
                    AlphaAt(symbolic, point.X, point.Y),
                    Is.EqualTo(AlphaAt(finite, point.X, point.Y)).Within(1e-3f),
                    $"symbolic and finite relative-origin transforms differ at {point}");
            }
        });
    }

    [Test]
    public void UnknownLegacy_FiniteLegacyParentRecomputesBoundsAndHitTestFromResolvedInput()
    {
        var unknown = new SymbolicDomainFilterEffect();
        var finite = new FiniteLegacyFilterEffect();
        using FilterEffectRenderNode root = CreateFilter(finite, unknown, new Rect(5, 6, 20, 12));

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        RenderFragmentReference[] legacy = References(compiled.Graph).Values
            .Where(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect)
            .ToArray();
        RenderFragmentReference unknownFragment = legacy.Single(static reference =>
            reference.BoundsRequirement == RenderFragmentBoundsRequirement.OwningTargetDomain);
        RenderFragmentReference finiteFragment = legacy.Single(static reference =>
            reference.BoundsRequirement == RenderFragmentBoundsRequirement.Finite);
        Rect inflatedDomain = s_rootDomain.Inflate(new Thickness(2));

        Assert.Multiple(() =>
        {
            Assert.That(finite.ObservedInputBounds.IsInvalid, Is.True,
                "Legacy authoring must not observe provisional finite bounds from a symbolic input.");
            Assert.That(unknownFragment.Bounds, Is.EqualTo(s_rootDomain));
            Assert.That(finiteFragment.RecordedBounds, Is.EqualTo(new Rect(3, 4, 24, 16)));
            Assert.That(finiteFragment.Bounds, Is.EqualTo(inflatedDomain));
            Assert.That(finiteFragment.HitTest(new Point(-1, 30)), Is.True);
            Assert.That(compiled.Measurement.OutputBounds, Is.EqualTo(s_rootDomain));
        });
    }

    [Test]
    public void UnknownLegacy_KeepsShaderAndGeometrySuffixInOneOpaqueSegment()
    {
        var effect = new SymbolicDomainFilterEffect { AppendTypedSuffix = true };
        using FilterEffectRenderNode root = CreateFilter(effect, new Rect(5, 6, 20, 12));

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        RenderFragmentReference[] references = References(compiled.Graph).Values.ToArray();
        RenderFragmentReference legacy = references
            .Single(static reference => reference.Kind == RenderFragmentKind.LegacyFilterEffect);
        var payload = (LegacyFilterEffectRenderFragmentPayload)legacy.Payload!;
        IFEItem[] items = payload.Context.Registry.Use(
            payload.Context,
            static context => context.GetOrderedItems().ToArray());

        Assert.Multiple(() =>
        {
            Assert.That(references.Any(static reference =>
                reference.Kind is RenderFragmentKind.Shader or RenderFragmentKind.Geometry), Is.False);
            Assert.That(items, Has.Length.EqualTo(3));
            Assert.That(items[0], Is.InstanceOf<IFEItem_Custom>());
            Assert.That(items[1], Is.InstanceOf<FEItem_Shader>());
            Assert.That(items[2], Is.InstanceOf<FEItem_Geometry>());
            Assert.That(legacy.Bounds, Is.EqualTo(s_rootDomain));
        });
    }

    [Test]
    public void BuiltInBackdrop_DerivedShaderGeometryFanOut_UsesProducerDomain()
    {
        var producerDomain = new Rect(0, 0, 40, 30);
        var secondConsumerDomain = new Rect(20, 0, 40, 30);
        using var root = new BuiltInDerivedFanOutNode(producerDomain, secondConsumerDomain);

        using CompiledRenderRequest compiled = Compile(root, targetDomain: null);
        RenderFragmentReference[] references = References(compiled.Graph).Values.ToArray();
        RenderFragmentReference capture = references.Single(static reference =>
            reference.Kind == RenderFragmentKind.BuiltInBackdropCapture);
        RenderFragmentReference shader = references.Single(static reference =>
            reference.Kind == RenderFragmentKind.Shader);
        RenderFragmentReference geometry = references.Single(static reference =>
            reference.Kind == RenderFragmentKind.Geometry);
        RenderFragmentReference[] layers = references
            .Where(static reference => reference.Kind == RenderFragmentKind.Layer)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(capture.Bounds, Is.EqualTo(producerDomain));
            Assert.That(shader.Bounds, Is.EqualTo(producerDomain));
            Assert.That(geometry.Bounds, Is.EqualTo(producerDomain));
            Assert.That(layers.Single(reference =>
                    ((LayerRenderFragmentPayload)reference.Payload!).Domain == producerDomain).Bounds,
                Is.EqualTo(producerDomain));
            Assert.That(layers.Single(reference =>
                    ((LayerRenderFragmentPayload)reference.Payload!).Domain == secondConsumerDomain).Bounds,
                Is.EqualTo(producerDomain.Intersect(secondConsumerDomain)));
        });
    }

    [Test]
    public void UnknownLegacy_DerivedFanOutAcrossDifferentDomains_IsRejected()
    {
        var effect = new SymbolicDomainFilterEffect();
        using var root = new UnknownLegacyDerivedFanOutNode(
            effect,
            new Rect(0, 0, 40, 30),
            new Rect(20, 0, 40, 30));

        InvalidOperationException? error = Assert.Throws<InvalidOperationException>(() =>
        {
            using CompiledRenderRequest _ = Compile(root, targetDomain: null);
        });

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("two different owning target domains"));
            Assert.That(effect.CallbackCount, Is.Zero);
        });
    }

    [Test]
    public void BuiltInBackdrop_InsideFiniteLayer_ResolvesLayerDomainWithoutRootDomain()
    {
        var domain = new Rect(12, 8, 40, 30);
        using var root = new LayerRenderNode(domain);
        root.AddChild(new SnapshotBackdropRenderNode());

        using CompiledRenderRequest compiled = Compile(root, targetDomain: null);
        RenderFragmentReference capture = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.BuiltInBackdropCapture);
        TargetDependencyStep captureStep = compiled.TargetDependencies.Steps
            .Single(static step => step.Kind == TargetDependencyKind.Capture);
        TargetScopePlan captureScope = compiled.TargetDependencies.Scopes
            .Single(scope => scope.Id == captureStep.ScopeId);

        Assert.Multiple(() =>
        {
            Assert.That(capture.BoundsRequirement, Is.EqualTo(RenderFragmentBoundsRequirement.OwningTargetDomain));
            Assert.That(capture.Bounds, Is.EqualTo(domain));
            Assert.That(capture.EffectiveScale, Is.EqualTo(EffectiveScale.Unbounded));
            Assert.That(captureScope.ResolvedDomain, Is.EqualTo(domain));
        });
    }

    [Test]
    public void BuiltInBackdrop_UnderTranslation_UsesCaptureProducerLocalCoordinates()
    {
        using TransformRenderNode root = WrapInTranslation(new SnapshotBackdropRenderNode(), 10);

        using CompiledRenderRequest compiled = Compile(root, s_rootDomain);
        RenderFragmentReference capture = References(compiled.Graph).Values
            .Single(static reference => reference.Kind == RenderFragmentKind.BuiltInBackdropCapture);
        TargetDependencyStep captureStep = compiled.TargetDependencies.Steps
            .Single(static step => step.Kind == TargetDependencyKind.Capture);
        TargetScopePlan captureScope = compiled.TargetDependencies.Scopes
            .Single(scope => scope.Id == captureStep.ScopeId);
        var localDomain = new Rect(-10, 0, 100, 60);

        Assert.Multiple(() =>
        {
            Assert.That(captureScope.ResolvedDomain, Is.EqualTo(localDomain));
            Assert.That(capture.Bounds, Is.EqualTo(localDomain));
            Assert.That(compiled.Regions.GetMetadata(capture).Bounds, Is.EqualTo(localDomain));
        });
    }

    [Test]
    public void BuiltInBackdrop_UnderTranslation_CapturesResolvedExtentAtRuntime()
    {
        var probe = new BuiltInCaptureProbeNode();
        using var root = new ContainerRenderNode();
        root.AddChild(new RectangleRenderNode(s_rootDomain, Brushes.Resource.White, null));
        root.AddChild(WrapInTranslation(probe, 10));
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_rootDomain,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
            });
        using var target = new CpuRenderTarget(100, 60);
        using var canvas = new ImmediateCanvas(target);

        renderer.Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(probe.CaptureCount, Is.EqualTo(1));
            Assert.That(probe.CapturedDeviceSize, Is.EqualTo(new PixelSize(100, 60)));
            Assert.That(probe.CapturedDensity, Is.EqualTo(1));
        });
    }

    [Test]
    public void BuiltInBackdrop_InsideFiniteLayer_CapturesLayerExtentAtRuntime()
    {
        var domain = new Rect(12, 8, 40, 30);
        var probe = new BuiltInCaptureProbeNode();
        using var root = new LayerRenderNode(domain);
        root.AddChild(new RectangleRenderNode(domain, Brushes.Resource.White, null));
        root.AddChild(probe);
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = s_rootDomain,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
            });
        using var target = new CpuRenderTarget(100, 60);
        using var canvas = new ImmediateCanvas(target);

        renderer.Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(probe.CaptureCount, Is.EqualTo(1));
            Assert.That(probe.CapturedDeviceSize, Is.EqualTo(new PixelSize(40, 30)));
            Assert.That(probe.CapturedDensity, Is.EqualTo(1));
        });
    }

    private static FilterEffectRenderNode CreateFilter(
        SymbolicDomainFilterEffect effect,
        Rect inputBounds)
    {
        var node = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
        node.AddChild(new EllipseRenderNode(inputBounds, Brushes.Resource.White, null));
        return node;
    }

    private static FilterEffectRenderNode CreateFilter(
        FiniteLegacyFilterEffect outerEffect,
        SymbolicDomainFilterEffect innerEffect,
        Rect inputBounds)
    {
        var outer = new FilterEffectRenderNode(outerEffect.ToResource(CompositionContext.Default));
        outer.AddChild(CreateFilter(innerEffect, inputBounds));
        return outer;
    }

    private static TransformRenderNode WrapInTranslation(RenderNode child, float x)
    {
        var result = new TransformRenderNode(
            Matrix.CreateTranslation(x, 0),
            TransformOperator.Prepend);
        result.AddChild(child);
        return result;
    }

    private static TransformEffect CreateTargetIndependentHalfScaleEffect()
    {
        var transform = new ScaleTransform();
        transform.ScaleX.CurrentValue = 50;
        transform.ScaleY.CurrentValue = 50;
        var effect = new TransformEffect();
        effect.Transform.CurrentValue = transform;
        effect.ApplyToTarget.CurrentValue = false;
        return effect;
    }

    private static Bitmap RenderToBitmap(RenderNode root, Rect targetDomain)
    {
        PixelSize size = PixelRect.FromRect(targetDomain).Size;
        using var renderer = new RenderNodeRenderer(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = targetDomain,
                TargetFactory = new CpuTargetFactory(),
                UseRenderCache = false,
            });
        using var target = new CpuRenderTarget(size.Width, size.Height);
        using var canvas = new ImmediateCanvas(target);
        canvas.Clear();
        renderer.Render(canvas);
        return target.Snapshot();
    }

    private static CompiledRenderRequest Compile(
        RenderNode root,
        Rect? targetDomain,
        float outputScale = 1)
    {
        var request = new RenderRequest(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            targetDomain,
            outputScale: outputScale,
            cachePolicy: RenderCacheOptions.Disabled));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(root);
        return new RenderRequestCompiler().Compile(request, graph);
    }

    private static IReadOnlyDictionary<RenderFragmentId, RenderFragmentReference> References(
        RecordedRenderGraph graph)
        => graph.Fragments.ToDictionary(
            static fragment => fragment.Id,
            static fragment => (RenderFragmentReference)fragment.Payload!);

    private static float AlphaAt(Bitmap bitmap, int x, int y)
    {
        Span<ushort> row = bitmap.GetRow<ushort>(y);
        return (float)BitConverter.UInt16BitsToHalf(row[(x * 4) + 3]);
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(PixelSize deviceSize)
            => new CpuRenderTarget(deviceSize.Width, deviceSize.Height);
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

    private sealed class BuiltInCaptureProbeNode : RenderNode, IBuiltInBackdropCaptureSink
    {
        public int CaptureCount { get; private set; }

        public PixelSize CapturedDeviceSize { get; private set; }

        public float CapturedDensity { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.DisableRenderCache();
            context.Publish(context.BuiltInBackdropCapture(this));
        }

        void IBuiltInBackdropCaptureSink.CommitBackdropCapture(Bitmap bitmap, float density)
        {
            CaptureCount++;
            CapturedDeviceSize = new PixelSize(bitmap.Width, bitmap.Height);
            CapturedDensity = density;
            bitmap.Dispose();
        }
    }

    private sealed class BuiltInDerivedFanOutNode(
        Rect producerDomain,
        Rect secondConsumerDomain)
        : RenderNode, IBuiltInBackdropCaptureSink
    {
        private const string IdentityShader = "half4 apply(half4 color) { return color; }";

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle capture = context.BuiltInBackdropCapture(this);
            RenderFragmentHandle shader = context.Shader(
                capture,
                ShaderDescription.CurrentPixel(IdentityShader));
            RenderFragmentHandle geometry = context.Geometry(
                shader,
                GeometryDescription.Create(
                    static _ => { },
                    RenderBoundsContract.Identity,
                    RenderHitTestContract.AnyInput,
                    structuralKey: typeof(BuiltInDerivedFanOutNode)));
            RenderFragmentHandle contributing = context.ContributeValues(geometry);
            context.Publish(context.Layer([contributing], producerDomain));
            context.Publish(context.Layer([contributing], secondConsumerDomain));
        }

        void IBuiltInBackdropCaptureSink.CommitBackdropCapture(Bitmap bitmap, float density)
            => bitmap.Dispose();
    }

    private sealed class UnknownLegacyDerivedFanOutNode : RenderNode
    {
        private const string IdentityShader = "half4 apply(half4 color) { return color; }";
        private readonly FilterEffectRenderNode _filter;
        private readonly Rect _firstDomain;
        private readonly Rect _secondDomain;

        public UnknownLegacyDerivedFanOutNode(
            SymbolicDomainFilterEffect effect,
            Rect firstDomain,
            Rect secondDomain)
        {
            _filter = CreateFilter(effect, new Rect(5, 6, 20, 12));
            _firstDomain = firstDomain;
            _secondDomain = secondDomain;
        }

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle legacy = context.RecordSubtree(_filter).Single();
            RenderFragmentHandle derived = context.Shader(
                legacy,
                ShaderDescription.CurrentPixel(IdentityShader));
            context.Publish(context.Layer([derived], _firstDomain));
            context.Publish(context.Layer([derived], _secondDomain));
        }

        protected override void OnDispose(bool disposing)
        {
            _filter.Dispose();
            base.OnDispose(disposing);
        }
    }
}

[SuppressResourceClassGeneration]
internal sealed partial class SymbolicDomainFilterEffect : FilterEffect
{
    private const string IdentityShader = "half4 apply(half4 color) { return color; }";

    public int CallbackCount { get; private set; }

    public bool AppendTypedSuffix { get; init; }

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        context.CustomEffect(Unit.Default, (_, _) => CallbackCount++);
        if (!AppendTypedSuffix)
            return;

        context.Shader(ShaderDescription.CurrentPixel(IdentityShader));
        context.Geometry(GeometryDescription.Create(
            static _ => { },
            RenderBoundsContract.Identity,
            RenderHitTestContract.AnyInput,
            structuralKey: typeof(SymbolicDomainFilterEffect)));
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource;
}

[SuppressResourceClassGeneration]
internal sealed partial class FiniteLegacyFilterEffect : FilterEffect
{
    public Rect ObservedInputBounds { get; private set; }

    public override void ApplyTo(FilterEffectContext context, FilterEffect.Resource resource)
    {
        ObservedInputBounds = context.Bounds;
        context.CustomEffect(
            Unit.Default,
            static (_, _) => { },
            static (_, bounds) => bounds.Inflate(new Thickness(2)));
    }

    public override Resource ToResource(CompositionContext context)
    {
        var resource = new Resource();
        bool updateOnly = true;
        resource.Update(this, context, ref updateOnly);
        return resource;
    }

    public new sealed class Resource : FilterEffect.Resource;
}
