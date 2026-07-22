using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class ShaderAndAllocationFailureTests
{
    [Test]
    public void DescriptionValidationFailure_HappensDuringRecordingBeforeAnyTargetAllocation()
    {
        using var node = new InvalidDescriptionNode();
        var factory = new TrackingTargetFactory();
        using var renderer = CreateRenderer(node, factory);

        ArgumentException? failure = Assert.Throws<ArgumentException>(() => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("CurrentPixel"));
            Assert.That(factory.CreateCalls, Is.Zero);
            Assert.That(factory.Targets, Is.Empty);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.Zero);
        });
    }

    [Test]
    public void SnippetMergeFailure_DoesNotPoisonAValidDeterministicRetry()
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }");
        var stage = new SkslSnippetStage(description);
        SkslMergedProgram before = SkslSnippetMerger.Merge([stage]);

        ArgumentException? failure = Assert.Throws<ArgumentException>(
            () => SkslSnippetMerger.Merge([]));
        SkslMergedProgram after = SkslSnippetMerger.Merge([stage]);

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("At least one CurrentPixel stage"));
            Assert.That(after.Source, Is.EqualTo(before.Source));
            Assert.That(after.Bindings, Is.EqualTo(before.Bindings));
            Assert.That(after.Identity, Is.EqualTo(before.Identity));
        });
    }

    [Test]
    public void InvalidProgram_PreservesValidationFailureAndReturnsEveryTargetToThePool()
    {
        using var node = new ShaderNode(ShaderDescription.WholeSource(
            "uniform shader src; half4 main(float2 p) { this is not valid SkSL; }",
            RenderBoundsContract.Identity));
        var factory = new TrackingTargetFactory();
        var renderer = CreateRenderer(node, factory);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.StartWith("SkSL program validation failed:"));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(renderer.TargetPoolStatistics.OwnedTargets, Is.GreaterThan(0));
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.Zero,
                "A failed backend compile must not install a program-cache entry.");
            Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => !target.IsDisposed));
        });

        renderer.Dispose();
        Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => target.DisposeCalls == 1));
    }

    [TestCase(RuntimeBindingFailure.MissingUniform)]
    [TestCase(RuntimeBindingFailure.DuplicateUniform)]
    [TestCase(RuntimeBindingFailure.MissingResource)]
    [TestCase(RuntimeBindingFailure.DuplicateResource)]
    public void RuntimeBindingFailure_SealsTheWriterAndDischargesEveryTarget(
        RuntimeBindingFailure failurePoint)
    {
        using var node = new RuntimeBindingFailureNode(failurePoint);
        var factory = new TrackingTargetFactory();
        var renderer = CreateRenderer(node, factory);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("must set its writer exactly once"));
            Assert.That(node.VerifyRetainedWriter is not null, Is.True);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.EqualTo(1),
                "A runtime binding failure must not corrupt the immutable compiled program.");
        });
        Action verifyRetainedWriter = node.VerifyRetainedWriter
            ?? throw new AssertionException("The runtime binder did not expose its retained-writer probe.");
        Assert.That(verifyRetainedWriter, Throws.TypeOf<InvalidOperationException>());

        renderer.Dispose();
        Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => target.DisposeCalls == 1));
    }

    [Test]
    public void MaterializationCallbackFailure_DischargesTheCreatedOutputWithoutPartialPublication()
    {
        var primary = new InvalidOperationException("materialization-callback-primary");
        using var node = new MaterializationFailureNode(primary);
        var factory = new TrackingTargetFactory();
        var renderer = CreateRenderer(node, factory);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(node.CallbackEntries, Is.EqualTo(1));
            Assert.That(factory.CreateCalls, Is.EqualTo(2),
                "The root and callback output were both acquired before the injected callback fault.");
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(node.Cache.IsCached, Is.False);
            Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => !target.IsDisposed));
        });

        renderer.Dispose();
        Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => target.DisposeCalls == 1));
    }

    [Test]
    public void UniformProviderFailure_RemainsPrimaryAndDoesNotPublishARasterization()
    {
        ShaderDescription description = ShaderDescription.CurrentPixel(
            "uniform float gain; half4 apply(half4 color) { return color * gain; }",
            bindings => bindings.Uniform(
                "gain",
                0.5f,
                static (_, _, _) => throw new InvalidOperationException("uniform-provider-failure"),
                structuralKey: "throwing-uniform-provider",
                runtimeIdentity: new RenderRuntimeIdentity("throwing-uniform-runtime")));
        using var node = new ShaderNode(description);
        var factory = new TrackingTargetFactory();
        var renderer = CreateRenderer(node, factory);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Is.EqualTo("uniform-provider-failure"));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.EqualTo(1),
                "A provider failure must not corrupt the immutable compiled program.");
        });

        renderer.Dispose();
        Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => target.DisposeCalls == 1));
    }

    [Test]
    public void TargetAcquisitionFailure_DischargesTheAlreadyAcceptedRootWithoutPartialOutput()
    {
        using var node = new ShaderNode(ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }"));
        var factory = new TrackingTargetFactory(failAt: 1);
        var renderer = CreateRenderer(node, factory);

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => renderer.Rasterize());

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Does.Contain("could not allocate"));
            Assert.That(factory.CreateCalls, Is.EqualTo(2));
            Assert.That(factory.Targets, Has.Count.EqualTo(1));
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(factory.Targets.Single().IsDisposed, Is.False,
                "An accepted target remains renderer-owned until renderer disposal.");
        });

        renderer.Dispose();
        Assert.That(factory.Targets.Single().DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void ProgramCacheDisposal_ContinuesAfterEachProgramFaultAndSurfacesTheFirstFailure()
    {
        var cache = new ProgramCache<ThrowingProgram>(
            static _ => { },
            static _ => 1,
            maxRetainedBytes: 16);
        var programs = new List<ThrowingProgram>();
        ProgramCacheContextKey context = new(
            "device",
            "context",
            "capability",
            "linear-premul-rgba16f",
            "options");
        Acquire("half4 main(float2 p) { return half4(1); }");
        Acquire("half4 main(float2 p) { return half4(0); }");

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(cache.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(failure!.Message, Is.EqualTo("program-dispose-1"),
                "The most-recently-used program is the first disposal attempt.");
            Assert.That(programs.Select(static program => program.DisposeCalls), Is.All.EqualTo(1));
            Assert.That(cache.Statistics.RetainedPrograms, Is.Zero);
            Assert.DoesNotThrow(cache.Dispose);
        });

        void Acquire(string source)
        {
            var identity = new SkslMergedProgramIdentity(
                source,
                [],
                SkslBackendBudget.Unlimited);
            using ProgramCacheLease<ThrowingProgram> lease = cache.GetOrCreate(
                identity,
                context,
                () =>
                {
                    var program = new ThrowingProgram(programs.Count);
                    programs.Add(program);
                    return program;
                });
        }
    }

    [Test]
    public void ProgramCreationFailure_LeavesNoEntryAndAValidRetryCanBeRetained()
    {
        using var cache = new ProgramCache<TrackingProgram>(
            static _ => { },
            static _ => 1,
            maxRetainedBytes: 16);
        var identity = new SkslMergedProgramIdentity(
            "half4 main(float2 p) { return half4(1); }",
            [],
            SkslBackendBudget.Unlimited);
        var context = new ProgramCacheContextKey(
            "device",
            "context",
            "capability",
            "linear-premul-rgba16f",
            "options");
        var primary = new InvalidOperationException("program-creation-primary");

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(
            () => cache.GetOrCreate(identity, context, () => throw primary));
        var recovered = new TrackingProgram();
        using (ProgramCacheLease<TrackingProgram> lease = cache.GetOrCreate(
                   identity,
                   context,
                   () => recovered))
        {
            Assert.That(lease.IsCacheHit, Is.False);
        }

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(primary));
            Assert.That(cache.Statistics.Misses, Is.EqualTo(2));
            Assert.That(cache.Statistics.Creations, Is.EqualTo(1));
            Assert.That(cache.Statistics.RetainedPrograms, Is.EqualTo(1));
        });

        cache.Dispose();
        Assert.That(recovered.DisposeCalls, Is.EqualTo(1));
    }

    [Test]
    public void RendererDisposal_ContinuesAfterPoolFaultAndStillReleasesProgramsAndPlans()
    {
        var poolFailure = new InvalidOperationException("renderer-pool-dispose-primary");
        using var node = new ShaderNode(ShaderDescription.CurrentPixel(
            "half4 apply(half4 color) { return color; }"));
        var factory = new TrackingTargetFactory(
            disposeFailureAt: index => index == 0 ? poolFailure : null);
        var renderer = CreateRenderer(node, factory);
        using (RenderNodeRasterization rasterization = renderer.Rasterize())
        {
            Assert.That(rasterization.IsEmpty, Is.False);
        }
        Assert.Multiple(() =>
        {
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.EqualTo(1));
            Assert.That(renderer.StructuralPlanCacheStatistics.RetainedPlans, Is.EqualTo(1));
            Assert.That(factory.Targets, Has.Count.GreaterThanOrEqualTo(2));
        });

        InvalidOperationException? failure = Assert.Throws<InvalidOperationException>(renderer.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(failure, Is.SameAs(poolFailure));
            Assert.That(renderer.TargetPoolStatistics.OwnedTargets, Is.Zero);
            Assert.That(renderer.TargetPoolStatistics.LeasedTargets, Is.Zero);
            Assert.That(renderer.ProgramCacheStatistics.RetainedPrograms, Is.Zero);
            Assert.That(renderer.StructuralPlanCacheStatistics.RetainedPlans, Is.Zero);
            Assert.That(factory.Targets, Has.All.Matches<TrackingRenderTarget>(target => target.DisposeCalls == 1));
            Assert.DoesNotThrow(renderer.Dispose);
        });
    }

    public enum RuntimeBindingFailure
    {
        MissingUniform,
        DuplicateUniform,
        MissingResource,
        DuplicateResource,
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode root, IRenderTargetFactory factory)
        => new(
            root,
            new RenderNodeRendererOptions
            {
                TargetDomain = new Rect(0, 0, 8, 8),
                OutputScale = 1,
                MaxWorkingScale = 1,
                UseRenderCache = false,
                TargetFactory = factory,
            });

    private sealed class ShaderNode(ShaderDescription description) : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            OpaqueRenderDescription source = OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 8, 8)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "shader-allocation-failure-source",
                runtimeIdentity: new RenderRuntimeIdentity("shader-allocation-failure-source-runtime"));
            context.Publish(context.Shader(context.OpaqueSource(source), description));
        }
    }

    private sealed class InvalidDescriptionNode : RenderNode
    {
        public override void Process(RenderNodeContext context)
        {
            _ = ShaderDescription.CurrentPixel(
                "half4 main(float2 position) { return half4(position, 0, 1); }");
        }
    }

    private sealed class RuntimeBindingFailureNode(RuntimeBindingFailure failurePoint) : RenderNode
    {
        private readonly object _resource = new();

        public Action? VerifyRetainedWriter { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            ShaderDescription description = failurePoint is RuntimeBindingFailure.MissingUniform
                or RuntimeBindingFailure.DuplicateUniform
                ? CreateUniformDescription()
                : CreateResourceDescription(context);
            RenderFragmentHandle source = context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    session.Publish(output);
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 8, 8)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: $"runtime-binding-source-{failurePoint}",
                runtimeIdentity: new RenderRuntimeIdentity($"runtime-binding-source-{failurePoint}")));
            context.Publish(context.Shader(source, description));
        }

        private ShaderDescription CreateUniformDescription()
        {
            return ShaderDescription.CurrentPixel(
                "uniform float gain; half4 apply(half4 color) { return color * gain; }",
                bindings => bindings.Uniform(
                    "gain",
                    0.5f,
                    (writer, value, _) =>
                    {
                        VerifyRetainedWriter = () => writer.Set(value);
                        if (failurePoint == RuntimeBindingFailure.MissingUniform)
                            return;

                        writer.Set(value);
                        writer.Set(value);
                    },
                    structuralKey: $"runtime-uniform-{failurePoint}",
                    runtimeIdentity: new RenderRuntimeIdentity($"runtime-uniform-{failurePoint}")));
        }

        private ShaderDescription CreateResourceDescription(RenderNodeContext context)
        {
            RenderResource<object> resource = context.Borrow(
                _resource,
                cacheKey: "runtime-binding-resource",
                version: 1);
            return ShaderDescription.CurrentPixel(
                "uniform shader lookup; half4 apply(half4 color) { return lookup.eval(color.rg); }",
                bindings => bindings.Resource(
                    "lookup",
                    resource,
                    ShaderResourceCoordinateSpace.Value,
                    (writer, _, _) =>
                    {
                        VerifyRetainedWriter = () =>
                        {
                            using SKShader retained = SKShader.CreateColor(SKColors.White);
                            writer.Set(retained);
                        };
                        if (failurePoint == RuntimeBindingFailure.MissingResource)
                            return;

                        writer.Set(SKShader.CreateColor(SKColors.White));
                        using SKShader duplicate = SKShader.CreateColor(SKColors.Black);
                        writer.Set(duplicate);
                    },
                    structuralKey: $"runtime-resource-{failurePoint}",
                    runtimeIdentity: new RenderRuntimeIdentity($"runtime-resource-{failurePoint}")));
        }
    }

    private sealed class MaterializationFailureNode(InvalidOperationException failure) : RenderNode
    {
        public int CallbackEntries { get; private set; }

        public override void Process(RenderNodeContext context)
        {
            context.Publish(context.OpaqueSource(OpaqueRenderDescription.Create(
                session =>
                {
                    CallbackEntries++;
                    using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                    output.Canvas.Use(canvas => canvas.Clear(Colors.CornflowerBlue));
                    throw failure;
                },
                RenderOperationBoundsContract.Source(new Rect(0, 0, 8, 8)),
                RenderHitTestContract.OutputBounds,
                RenderValueCardinality.Single,
                RenderScaleContract.MaterializeAtWorkingScale,
                structuralKey: "materialization-callback-failure",
                runtimeIdentity: new RenderRuntimeIdentity("materialization-callback-failure"))));
        }
    }

    private sealed class TrackingTargetFactory(
        int? failAt = null,
        Func<int, Exception?>? disposeFailureAt = null) : IRenderTargetFactory
    {
        public List<TrackingRenderTarget> Targets { get; } = [];

        public int CreateCalls { get; private set; }

        public RenderTarget? Create(PixelSize deviceSize)
        {
            int index = CreateCalls++;
            if (index == failAt)
                return null;

            var target = new TrackingRenderTarget(deviceSize, disposeFailureAt?.Invoke(index));
            Targets.Add(target);
            return target;
        }
    }

    private sealed class TrackingRenderTarget : RenderTarget
    {
        private readonly Exception? _disposeFailure;

        public TrackingRenderTarget(PixelSize size, Exception? disposeFailure = null)
            : base(
                SKSurface.Create(new SKImageInfo(
                    size.Width,
                    size.Height,
                    SKColorType.RgbaF16,
                    SKAlphaType.Premul,
                    SKColorSpace.CreateSrgbLinear())),
                size.Width,
                size.Height)
        {
            _disposeFailure = disposeFailure;
        }

        public int DisposeCalls { get; private set; }

        protected override void Dispose(bool disposing)
        {
            bool fail = disposing && !IsDisposed && _disposeFailure is not null;
            if (disposing && !IsDisposed)
                DisposeCalls++;
            base.Dispose(disposing);
            if (fail)
                throw _disposeFailure!;
        }
    }

    private sealed class TrackingProgram : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
        }
    }

    private sealed class ThrowingProgram(int id) : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose()
        {
            DisposeCalls++;
            throw new InvalidOperationException($"program-dispose-{id}");
        }
    }
}
