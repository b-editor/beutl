using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

[TestFixture]
public sealed class StructuralAndProgramCacheTests
{
    private const string FirstSource =
        "uniform float gain; half4 apply(half4 color) { return color * gain; }";
    private const string SecondSource =
        "uniform float gain; half4 apply(half4 color) { return half4(color.rgb * gain, color.a); }";

    [Test]
    public void ParameterOnlyAnimation_ReusesOneStructuralPlanForOneHundredFrames()
    {
        using var source = new CpuRenderTarget(8, 8);
        source.Value.Canvas.Clear(new SKColor(160, 96, 32, 224));
        using Bitmap sourceBitmap = source.Snapshot();
        using var node = new ExecutableParameterShaderNode(source);
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 8, 8),
                    OutputScale = 1,
                    MaxWorkingScale = 1,
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });
        ushort[]? firstPixels = null;
        ushort[]? finalPixels = null;

        for (int frame = 0; frame < 100; frame++)
        {
            node.Value = frame / 100f;
            using RenderNodeRasterization raster = renderer.Rasterize();
            Assert.That(raster.Bitmap, Is.Not.Null);
            if (frame == 0)
                firstPixels = raster.Bitmap!.GetPixelSpan<ushort>().ToArray();
            if (frame == 99)
                finalPixels = raster.Bitmap!.GetPixelSpan<ushort>().ToArray();
        }

        StructuralPlanCacheStatistics statistics = renderer.StructuralPlanCacheStatistics;
        double maximumFinalDifference = MaximumScaledDifference(sourceBitmap, finalPixels!, 0.99f);
        Assert.Multiple(() =>
        {
            Assert.That(statistics.Compilations, Is.EqualTo(1));
            Assert.That(statistics.Misses, Is.EqualTo(1));
            Assert.That(statistics.Hits, Is.EqualTo(99));
            Assert.That(statistics.Replacements, Is.Zero);
            Assert.That(renderer.ProgramCacheStatistics.Creations, Is.EqualTo(1));
            Assert.That(renderer.ProgramCacheStatistics.Misses, Is.EqualTo(1));
            Assert.That(renderer.ProgramCacheStatistics.Hits, Is.EqualTo(99));
            Assert.That(renderer.LastExecutionStatistics.ProgramCacheHits, Is.EqualTo(1));
            Assert.That(finalPixels, Is.Not.EqualTo(firstPixels),
                "a warmed plan and program must bind the current frame's direct uniform value");
            Assert.That(finalPixels, Has.Some.Not.Zero,
                "the final animated frame must produce a non-vacuous result");
            Assert.That(maximumFinalDifference, Is.LessThan(0.002),
                "the final warmed frame must bind the authored 0.99 direct-uniform value");
        });
    }

    private static double MaximumScaledDifference(Bitmap source, ushort[] actual, float scale)
    {
        ReadOnlySpan<ushort> expected = source.GetPixelSpan<ushort>();
        Assert.That(actual, Has.Length.EqualTo(expected.Length));
        double maximum = 0;
        for (int index = 0; index < actual.Length; index++)
        {
            float sourceValue = (float)BitConverter.UInt16BitsToHalf(expected[index]);
            float actualValue = (float)BitConverter.UInt16BitsToHalf(actual[index]);
            maximum = Math.Max(maximum, Math.Abs(actualValue - (sourceValue * scale)));
        }

        return maximum;
    }

    [Test]
    public void BoundsOnlyRuntimeChange_RebindsCurrentBoundsWithoutRecompiling()
    {
        using var cache = new StructuralPlanCache();
        using var node = new ParameterShaderNode
        {
            Bounds = new Rect(1, 2, 8, 6),
        };

        using (CompiledRenderRequest first = Compile(cache, node))
        {
            Assert.That(first.ExecutionPlan.ShaderRuns.Single().Output.Bounds, Is.EqualTo(node.Bounds));
        }

        node.Bounds = new Rect(4, 3, 17, 11);
        using CompiledRenderRequest second = Compile(cache, node);

        Assert.Multiple(() =>
        {
            Assert.That(second.ExecutionPlan.ShaderRuns.Single().Output.Bounds, Is.EqualTo(node.Bounds));
            Assert.That(second.SelectedOutputBounds, Is.EqualTo(node.Bounds));
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
        });
    }

    [Test]
    public void DeclaredStructuralToggle_CompilesExactlyOneReplacement()
    {
        using var cache = new StructuralPlanCache();
        using var node = new ParameterShaderNode();

        using (Compile(cache, node))
        {
        }

        node.StructuralVariant = 1;
        using (Compile(cache, node))
        {
        }
        using (Compile(cache, node))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
            Assert.That(cache.Statistics.Replacements, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
        });
    }

    [Test]
    public void CustomBinder_SnapshotValueIsRuntimeOnly_BinderKeyIsStructural()
    {
        using var cache = new StructuralPlanCache();
        using var node = new ParameterShaderNode
        {
            UseCustomBinder = true,
            BinderStructuralKey = "binder-v1",
        };

        using (Compile(cache, node))
        {
        }

        node.Value = 0.75f;
        using (Compile(cache, node))
        {
        }

        Assert.That(cache.Statistics.Hits, Is.EqualTo(1),
            "custom binder snapshot values must not enter structural identity");

        node.BinderStructuralKey = "binder-v2";
        using (Compile(cache, node))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Replacements, Is.EqualTo(1));
        });
    }

    [Test]
    public void FusionMode_IsPartOfStructuralIdentity()
    {
        using var cache = new StructuralPlanCache();
        using var node = new ParameterShaderNode();

        using (Compile(cache, node, FusionMode.Enabled))
        {
        }
        using (Compile(cache, node, FusionMode.Disabled))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
            Assert.That(cache.Statistics.Replacements, Is.EqualTo(1));
        });
    }

    [Test]
    public void OpacityBoundsEligibilityChange_ReplacesStructuralPlan()
    {
        var inputBounds = new Rect(2, 3, 12, 8);
        AssertOpacityEligibilityChangeReplacesStructuralPlan(
            inputBounds,
            new Rect(2, 3, 11, 8),
            EffectiveScale.Unbounded,
            EffectiveScale.Unbounded);
    }

    [Test]
    public void OpacityScaleEligibilityChange_ReplacesStructuralPlan()
    {
        var bounds = new Rect(2, 3, 12, 8);
        AssertOpacityEligibilityChangeReplacesStructuralPlan(
            bounds,
            bounds,
            EffectiveScale.Unbounded,
            EffectiveScale.At(1));
    }

    [Test]
    public void ForcedHashCollision_UsesFullIdentityAndThenWarmsReplacement()
    {
        using var cache = new StructuralPlanCache();
        using var firstNode = new ParameterShaderNode { StructuralVariant = 0 };
        using var secondNode = new ParameterShaderNode { StructuralVariant = 1 };
        using var equivalentNode = new ParameterShaderNode { StructuralVariant = 1, Value = 0.8f };
        using RenderRequest firstRequest = CreateRequest(FusionMode.Enabled);
        using RenderRequest secondRequest = CreateRequest(FusionMode.Enabled);
        using RenderRequest equivalentRequest = CreateRequest(FusionMode.Enabled);
        RecordedRenderGraph firstGraph = new RenderRequestRecorder(firstRequest).Record(firstNode);
        RecordedRenderGraph secondGraph = new RenderRequestRecorder(secondRequest).Record(secondNode);
        RecordedRenderGraph equivalentGraph = new RenderRequestRecorder(equivalentRequest).Record(equivalentNode);
        StructuralPlanIdentity firstIdentity = StructuralPlanIdentity.Create(
            firstRequest.Options.PlanIdentity,
            firstGraph,
            SkslBackendBudget.Unlimited);
        StructuralPlanIdentity secondIdentity = StructuralPlanIdentity.Create(
            secondRequest.Options.PlanIdentity,
            secondGraph,
            SkslBackendBudget.Unlimited);
        StructuralPlanIdentity equivalentIdentity = StructuralPlanIdentity.Create(
            equivalentRequest.Options.PlanIdentity,
            equivalentGraph,
            SkslBackendBudget.Unlimited);
        const int forcedBucket = 0x1234;

        _ = GetOrCompile(cache, firstIdentity, firstGraph, forcedBucket);
        _ = GetOrCompile(cache, secondIdentity, secondGraph, forcedBucket);
        ExecutionIslandPlan warmed = GetOrCompile(
            cache,
            equivalentIdentity,
            equivalentGraph,
            forcedBucket);

        Assert.Multiple(() =>
        {
            Assert.That(firstIdentity, Is.Not.EqualTo(secondIdentity));
            Assert.That(secondIdentity, Is.EqualTo(equivalentIdentity));
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
            Assert.That(cache.Statistics.Replacements, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
            Assert.That(
                warmed.ShaderRuns.Single().Stages.Single().Description.CreateRuntimeIdentity(),
                Is.EqualTo(equivalentNode.LastDescription!.CreateRuntimeIdentity()),
                "the warmed collision-safe template must bind the current request's runtime description");
        });
    }

    [Test]
    public void Renderer_PersistsStructuralCacheAcrossRequests()
    {
        using var node = new EmptyNode();
        using var renderer = new RenderNodeRenderer(node);

        using (renderer.Rasterize())
        {
        }
        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(renderer.StructuralPlanCacheStatistics.Compilations, Is.EqualTo(1));
            Assert.That(renderer.StructuralPlanCacheStatistics.Hits, Is.EqualTo(1));
        });
    }

    [Test]
    public void RenderInputReadbackSelection_ReplacesStructuralPlanAndRebindsSnapshots()
    {
        using var node = new MutableTargetCommandReadbackNode();
        using var renderer = new RenderNodeRenderer(
            node,
            new RenderNodeRendererOptions
            {
                DefaultRequest = new RenderNodeRenderRequest
                {
                    TargetDomain = new Rect(0, 0, 8, 8),
                    CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
                },
                TargetFactory = new CpuTargetFactory(),
            });

        using (renderer.Rasterize())
        {
        }

        node.ReadFirstInput = false;
        using (renderer.Rasterize())
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(node.SnapshotCounts, Is.EqualTo(new[] { 1, 1 }));
            Assert.That(renderer.StructuralPlanCacheStatistics.Compilations, Is.EqualTo(2));
            Assert.That(renderer.StructuralPlanCacheStatistics.Misses, Is.EqualTo(2));
            Assert.That(renderer.StructuralPlanCacheStatistics.Replacements, Is.EqualTo(1));
            Assert.That(renderer.StructuralPlanCacheStatistics.Hits, Is.Zero);
        });
    }

    [Test]
    public void NestedRequestFamily_ReusesEveryCurrentPlanAndTrimsRemovedMembers()
    {
        using var cache = new StructuralPlanCache();
        using var child = new EmptyNode();
        using var nested = new NestedParentNode(child);
        using var flat = new EmptyNode();

        using (Compile(cache, nested))
        {
        }
        using (Compile(cache, nested))
        {
        }

        StructuralPlanCacheStatistics warmed = cache.Statistics;
        Assert.Multiple(() =>
        {
            Assert.That(warmed.Compilations, Is.EqualTo(2));
            Assert.That(warmed.Misses, Is.EqualTo(2));
            Assert.That(warmed.Hits, Is.EqualTo(2));
            Assert.That(warmed.Replacements, Is.Zero);
            Assert.That(warmed.RetainedPlans, Is.EqualTo(2));
        });

        using (Compile(cache, flat))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Hits, Is.EqualTo(3));
            Assert.That(cache.Statistics.RetainedPlans, Is.EqualTo(1));
        });
    }

    [Test]
    public void TargetLayerScope_EmptyRegionClass_CompilesOneReplacement()
    {
        using var cache = new StructuralPlanCache();
        using var node = new MutableTargetLayerScopeNode();

        using (Compile(cache, node))
        {
        }

        node.Region = TargetRegion.Region(new Rect(0, 0, 8, 8));
        using (Compile(cache, node))
        {
        }
        using (Compile(cache, node))
        {
        }

        Assert.Multiple(() =>
        {
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
            Assert.That(cache.Statistics.Replacements, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.EqualTo(1));
        });
    }

    private static ExecutionIslandPlan GetOrCompile(
        StructuralPlanCache cache,
        StructuralPlanIdentity identity,
        RecordedRenderGraph graph,
        int forcedBucket)
    {
        var planner = new ExecutionIslandPlanner();
        return cache.GetOrCompile(
            identity,
            graph,
            () => planner.Plan(
                graph,
                RenderRequestCompiler.ResolveRoots(graph),
                FusionMode.Enabled,
                SkslBackendBudget.Unlimited),
            forcedBucket);
    }

    private static void AssertOpacityEligibilityChangeReplacesStructuralPlan(
        Rect changedInputBounds,
        Rect changedOpacityBounds,
        EffectiveScale changedInputScale,
        EffectiveScale changedOpacityScale)
    {
        var eligibleBounds = new Rect(2, 3, 12, 8);
        using var cache = new StructuralPlanCache();
        using RenderRequest eligibleRequest = CreateRequest(FusionMode.Enabled);
        using RenderRequest changedRequest = CreateRequest(FusionMode.Enabled);
        RecordedRenderGraph eligibleGraph = CreateOpacityGraph(
            eligibleRequest.Id,
            eligibleBounds,
            eligibleBounds,
            EffectiveScale.Unbounded,
            EffectiveScale.Unbounded);
        RecordedRenderGraph changedGraph = CreateOpacityGraph(
            changedRequest.Id,
            changedInputBounds,
            changedOpacityBounds,
            changedInputScale,
            changedOpacityScale);
        StructuralPlanIdentity eligibleIdentity = StructuralPlanIdentity.Create(
            eligibleRequest.Options.PlanIdentity,
            eligibleGraph,
            SkslBackendBudget.Unlimited);
        StructuralPlanIdentity changedIdentity = StructuralPlanIdentity.Create(
            changedRequest.Options.PlanIdentity,
            changedGraph,
            SkslBackendBudget.Unlimited);
        const int forcedBucket = 0x4f50;

        _ = GetOrCompile(
            cache,
            eligibleIdentity,
            eligibleGraph,
            forcedBucket);
        _ = GetOrCompile(
            cache,
            changedIdentity,
            changedGraph,
            forcedBucket);

        Assert.Multiple(() =>
        {
            Assert.That(eligibleIdentity, Is.Not.EqualTo(changedIdentity));
            Assert.That(cache.Statistics.Compilations, Is.EqualTo(2));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
            Assert.That(cache.Statistics.Replacements, Is.EqualTo(1));
            Assert.That(cache.Statistics.Hits, Is.Zero);
        });
    }

    private static RecordedRenderGraph CreateOpacityGraph(
        RenderRequestId requestId,
        Rect inputBounds,
        Rect opacityBounds,
        EffectiveScale inputScale,
        EffectiveScale opacityScale)
    {
        var input = new RenderFragmentReference(
            RenderFragmentKind.ContributeValues,
            inputBounds,
            inputScale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [],
            payload: null,
            static _ => true);
        var opacity = new RenderFragmentReference(
            RenderFragmentKind.Opacity,
            opacityBounds,
            opacityScale,
            RenderValueCardinality.Single,
            contributesValuesToTarget: true,
            canBeUsedAsValueInput: true,
            hasTargetEffects: false,
            hasOpaqueExternalWork: false,
            [input],
            new OpacityRenderFragmentPayload(
                0.625f,
                OpacityRenderNode.CreateFusionDescription(0.625f)),
            static _ => true);
        var builder = new RecordedRenderGraphBuilder(requestId);
        RenderProvenanceId provenance = builder.AddProvenance(
            typeof(StructuralAndProgramCacheTests),
            "opacity-structural-cache-test");
        foreach (RenderFragmentReference reference in new[] { input, opacity })
        {
            RenderValueId[] inputs = reference.Inputs
                .SelectMany(static item => item.ValueIds)
                .ToArray();
            reference.ValueIds = [builder.AddValue(inputs, provenance, reference)];
            reference.Id = builder.AddFragment(reference.ValueIds, provenance, reference);
        }

        builder.PublishRoot(opacity.Id!.Value);
        return builder.Build();
    }

    private static CompiledRenderRequest Compile(
        StructuralPlanCache cache,
        RenderNode node,
        FusionMode fusionMode = FusionMode.Enabled)
    {
        RenderRequest request = CreateRequest(fusionMode);
        try
        {
            RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(node);
            return new RenderRequestCompiler(cache).Compile(request, graph);
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    private static RenderRequest CreateRequest(FusionMode fusionMode)
        => new(new RenderRequestOptions(
            RenderIntent.Preview,
            RenderRequestPurpose.Auxiliary,
            outputScale: 1,
            maxWorkingScale: 1,
            cachePolicy: RenderCacheOptions.Disabled,
            fusionMode: fusionMode));

    private sealed class ParameterShaderNode : RenderNode
    {
        public Rect Bounds { get; set; } = new(2, 3, 12, 8);

        public float Value { get; set; } = 0.25f;

        public int StructuralVariant { get; set; }

        public bool UseCustomBinder { get; set; }

        public object BinderStructuralKey { get; set; } = "binder-v1";

        public ShaderDescription? LastDescription { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription source = OpaqueRenderDescription.Create(
                ("source-frame", Value),
                static (_, _) => { },
                OpaqueRenderBoundsContract.Source(Bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "structural-cache-source");
            RenderFragmentHandle input = context.OpaqueSource(source);
            string shaderSource = StructuralVariant == 0 ? FirstSource : SecondSource;
            LastDescription = ShaderDescription.CurrentPixel(
                shaderSource,
                bindings =>
                {
                    if (UseCustomBinder)
                    {
                        bindings.Uniform(
                            "gain",
                            Value,
                            BindFloat,
                            BinderStructuralKey,
                            ShaderBindingCachePolicy.ReuseFromSnapshot);
                    }
                    else
                    {
                        bindings.Uniform("gain", Value);
                    }
                });
            context.Publish(context.Shader(input, LastDescription));
        }

        private static void BindFloat(
            ShaderUniformWriter writer,
            float value,
            ShaderExecutionContext context)
            => writer.Set(value);
    }

    private sealed class EmptyNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
        }
    }

    private sealed class NestedParentNode(RenderNode child) : RenderNode
    {
        public override void Process(RenderNodeContext context)
            => _ = context.RecordNestedTarget(child, new Rect(0, 0, 8, 8));
    }

    private sealed class MutableTargetLayerScopeNode : RenderNode
    {
        public TargetRegion Region { get; set; } = TargetRegion.Empty;

        public override void Process(RenderNodeContext context)
            => context.Publish(context.TargetLayerScope([], Region));
    }

    private sealed class MutableTargetCommandReadbackNode : RenderNode
    {
        private static readonly Rect s_bounds = new(0, 0, 8, 8);

        public bool ReadFirstInput { get; set; } = true;

        public int[] SnapshotCounts { get; } = new int[2];

        public override void Process(RenderNodeContext context)
        {
            RenderFragmentHandle first = context.OpaqueSource(CreateSource("first"));
            RenderFragmentHandle second = context.OpaqueSource(CreateSource("second"));
            int selectedInput = ReadFirstInput ? 0 : 1;
            RenderFragmentHandle command = context.TargetCommand(
                [first, second],
                TargetCommandDescription.CreateRequestLocal(
                    session => session.Inputs[selectedInput].UseSnapshot(
                        _ => SnapshotCounts[selectedInput]++),
                    TargetRegion.Empty,
                    Rect.Empty,
                    RenderHitTestContract.None,
                    inputReadbacks: ReadFirstInput
                        ? [RenderInputReadback.All, RenderInputReadback.None]
                        : [RenderInputReadback.None, RenderInputReadback.All],
                    structuralKey: "mutable-target-input-readback"));
            context.PublishRange([first, second, command]);
        }

        private static OpaqueRenderDescription CreateSource(string key)
            => OpaqueRenderDescription.CreateRequestLocal(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    session.Publish(output);
                },
                OpaqueRenderBoundsContract.Source(s_bounds),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: ("mutable-target-input-readback-source", key));
    }

    private sealed class ExecutableParameterShaderNode(RenderTarget source) : RenderNode
    {
        public float Value { get; set; }

        public override void Process(RenderNodeContext context)
        {
            RenderResource<RenderTarget> target = context.Borrow(
                source,
                "structural-program-cache-source",
                version: 1);
            RenderFragmentHandle input = context.MaterializedInput(
                MaterializedInputDescription.FromRenderTarget(
                    target,
                    new Rect(0, 0, 8, 8),
                    EffectiveScale.At(1),
                    new PixelRect(0, 0, 8, 8),
                    default,
                    RenderHitTestContract.OutputBounds));
            ShaderDescription shader = ShaderDescription.CurrentPixel(
                FirstSource,
                bindings => bindings.Uniform("gain", Value));
            context.Publish(context.Shader(input, shader));
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public int GetMaximumDimension(RenderTargetAllocationDescriptor allocation) => RenderScaleUtilities.MaxBufferDimension;

        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
            => new CpuRenderTarget(allocation.DeviceSize.Width, allocation.DeviceSize.Height);
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
