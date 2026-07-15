using System.Runtime.InteropServices;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.EffectPipeline;

[NonParallelizable]
[TestFixture]
public class PrimitiveRuntimeContractTests
{
    private static readonly Rect s_bounds = new(0, 0, 32, 32);

    private const string CopyShader = """
        #version 450
        layout(location = 0) in vec2 fragCoord;
        layout(location = 0) out vec4 outColor;
        layout(set = 0, binding = 0) uniform sampler2D srcTexture;
        layout(push_constant) uniform PC { float dummy; } pc;
        void main() { outColor = texture(srcTexture, fragCoord); }
        """;

    [StructLayout(LayoutKind.Sequential)]
    private struct PushConstants
    {
        public float Dummy;
    }

    [Test]
    public void DeliveryAllocationFailure_IsNotReplacedByInputDisposeFailure()
    {
        var cleanup = new InvalidOperationException("simulated input cleanup failure");
        var builder = new EffectGraphBuilder(s_bounds, outputScale: 1f, workingScale: 1f, renderIntent: RenderIntent.Delivery);
        builder.Shader(ShaderNodeDescriptor.Snippet("half4 apply(half4 c) { return c; }"));
        (CompiledPlan plan, FrameResources resources) = Compile(builder);
        using var pool = new RenderTargetPool();
        pool.SetBackingFactoryForTest(static (_, _) => null);
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            static _ => { },
            onDispose: () => throw cleanup);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, resources, [input], outputScale: 1f, workingScale: 1f,
            maxWorkingScale: float.PositiveInfinity, diagnostics: null, pool: pool, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.Not.SameAs(cleanup));
            Assert.That(actual!.Message, Does.Contain("Effect pass buffer allocation failed"));
        });
    }

    [Test]
    public void ComputeOutputAllocationFailure_PreviewDropsAfterInputCleanupFailure()
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute output-allocation cleanup contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                static _ => throw new AssertionException("dispatch must not run after output allocation fails"),
                passCount: 1,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                structuralToken: "compute-output-allocation-cleanup");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Preview).Compute(descriptor));
            using var pool = new RenderTargetPool();
            pool.SetBackingFactoryFailingAfterForTest(successfulAcquires: 1);
            bool inputDisposed = false;
            RenderNodeOperation input = RenderNodeOperation.CreateLambda(
                s_bounds,
                canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
                onDispose: () => inputDisposed = true);
            var cleanupFailure = new InvalidOperationException("compute materialized-input cleanup failed");

            IDisposable computeInputDisposeHook = PlanExecutor.UseTestHooks(hooks => hooks.ComputeInputDisposeFailure = cleanupFailure);
            try
            {
                RenderNodeOperation[] outputs = PlanExecutor.Execute(
                    plan, resources, [input], 1f, 1f, maxWorkingScale: 2f, diagnostics: null, pool, renderIntent: RenderIntent.Preview);
                try
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(outputs, Is.Empty,
                            "preview must keep the allocation-failure drop contract when cleanup also faults");
                        Assert.That(inputDisposed, Is.True,
                            "the detached source operation must still be consumed after materialized-input cleanup faults");
                        Assert.That(pool.LiveLeaseCount, Is.Zero);
                    });
                }
                finally
                {
                    RenderNodeOperation.DisposeAll(outputs);
                }
            }
            finally
            {
                computeInputDisposeHook.Dispose();
            }
        });
    }

    [Test]
    public void GeometryOutputAllocationFailure_PreviewDropsAfterInputCleanupFailure()
    {
        GeometryNodeDescriptor descriptor = GeometryNodeDescriptor.Create(
            static session => session.Inputs[0].Draw(session.OpenCanvas()),
            BoundsContract.Identity,
            structuralToken: "geometry-output-allocation-cleanup");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Preview).Geometry(descriptor));
        using var pool = new RenderTargetPool();
        pool.SetBackingFactoryFailingAfterForTest(successfulAcquires: 1);
        bool inputDisposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            onDispose: () => inputDisposed = true);
        var cleanupFailure = new InvalidOperationException("geometry materialized-input cleanup failed");

        IDisposable geometryInputDisposeHook = PlanExecutor.UseTestHooks(hooks => hooks.GeometryInputDisposeFailure = cleanupFailure);
        try
        {
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, maxWorkingScale: 2f, diagnostics: null, pool, renderIntent: RenderIntent.Preview);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(outputs, Is.Empty,
                        "preview must keep the allocation-failure drop contract when cleanup also faults");
                    Assert.That(inputDisposed, Is.True,
                        "the detached source operation must still be consumed after materialized-input cleanup faults");
                    Assert.That(pool.LiveLeaseCount, Is.Zero);
                });
            }
            finally
            {
                RenderNodeOperation.DisposeAll(outputs);
            }
        }
        finally
        {
            geometryInputDisposeHook.Dispose();
        }
    }

    [Test]
    public void ComputeCpuFallbackOutputAllocationFailure_PreviewDropsAfterInputCleanupFailure()
    {
        ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
            static _ => throw new AssertionException("dispatch must not run"),
            passCount: 1,
            BoundsContract.FullFrame,
            ComputeFallbackPolicy.Cpu(
                static _ => throw new AssertionException("callback must not run after allocation fails")),
            structuralToken: "compute-cpu-output-allocation-cleanup");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Preview).Compute(descriptor));
        using var pool = new RenderTargetPool();
        pool.SetBackingFactoryFailingAfterForTest(successfulAcquires: 1);
        bool inputDisposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            onDispose: () => inputDisposed = true);
        var cleanupFailure = new InvalidOperationException("CPU-fallback materialized-input cleanup failed");

        IDisposable computeFallbackHook = PlanExecutor.UseTestHooks(static hooks => hooks.ForceComputeFallback = true);
        IDisposable computeInputDisposeHook = PlanExecutor.UseTestHooks(hooks => hooks.ComputeInputDisposeFailure = cleanupFailure);
        try
        {
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, maxWorkingScale: 2f, diagnostics: null, pool, renderIntent: RenderIntent.Preview);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(outputs, Is.Empty,
                        "preview must keep the CPU-fallback allocation-failure drop contract when cleanup also faults");
                    Assert.That(inputDisposed, Is.True,
                        "the detached source operation must still be consumed after materialized-input cleanup faults");
                    Assert.That(pool.LiveLeaseCount, Is.Zero);
                });
            }
            finally
            {
                RenderNodeOperation.DisposeAll(outputs);
            }
        }
        finally
        {
            computeInputDisposeHook.Dispose();
            computeFallbackHook.Dispose();
        }
    }

    [TestCase(1, 0)]
    [TestCase(1, 2)]
    public void ComputeDispatchCount_MustExactlyMatchDeclaration(int declared, int actual)
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute pass-count contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var shader = GLSLShader.Create(CopyShader);
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                ctx =>
                {
                    for (int i = 0; i < actual; i++)
                    {
                        ctx.Run(shader, ctx.Source, ctx.Destination, new PushConstants());
                    }
                },
                declared,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                structuralToken: $"dispatch-count-{declared}-{actual}");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Compute(descriptor));
            using var pool = new RenderTargetPool();

            InvalidOperationException error = Assert.Catch<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery))!;
            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.Contain("dispatch"));
                Assert.That(error.Message, Does.Contain(declared.ToString()));
                Assert.That(pool.LiveLeaseCount, Is.Zero);
            });
        });
    }

    [Test]
    public void ComputeTerminalCopy_CompletesWithoutDeclaredDispatches()
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute terminal-copy contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                static ctx => ctx.CopySourceToDestination(),
                passCount: 3,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                structuralToken: "terminal-copy-success");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Compute(descriptor));
            using var pool = new RenderTargetPool();

            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery);
            RenderNodeOperation.DisposeAll(outputs);

            Assert.That(pool.LiveLeaseCount, Is.Zero);
        });
    }

    [Test]
    public void ComputeTerminalCopy_BackendFailureCannotDegradeToPreviewIdentity()
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute terminal-copy failure contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                static ctx => ctx.CopySourceToDestination(),
                passCount: 1,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                structuralToken: "terminal-copy-backend-failure",
                dispatchFailureBehavior: ComputeDispatchFailureBehavior.IdentityInPreview);
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Preview).Compute(descriptor));
            using var pool = new RenderTargetPool();
            var injected = new InvalidOperationException("copy backend failed");

            IDisposable computeCopyHook = PlanExecutor.UseTestHooks(hooks => hooks.ComputeCopyFailure = injected);
            try
            {
                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                    plan, resources, [Input()], 1f, 1f, maxWorkingScale: 2f, diagnostics: null, pool, renderIntent: RenderIntent.Preview));
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(injected),
                        "backend copy failures must bypass IdentityInPreview without wrapping");
                    Assert.That(pool.LiveLeaseCount, Is.Zero);
                });
            }
            finally
            {
                computeCopyHook.Dispose();
            }
        });
    }

    [Test]
    public void ComputeTerminalCopy_SamplingPreparationFailurePropagatesAndReleasesResources()
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute terminal-copy preparation contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                static ctx => ctx.CopySourceToDestination(),
                passCount: 1,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                structuralToken: "terminal-copy-prepare-failure");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Compute(descriptor));
            using var pool = new RenderTargetPool();
            var injected = new InvalidOperationException("copy destination preparation failed");

            IDisposable computeCopyPrepareHook = PlanExecutor.UseTestHooks(hooks => hooks.ComputeCopyPrepareFailure = injected);
            try
            {
                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                    plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery));
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(injected));
                    Assert.That(pool.LiveLeaseCount, Is.Zero);
                });
            }
            finally
            {
                computeCopyPrepareHook.Dispose();
            }
        });
    }

    [Test]
    public void DisposeDisposablesCapturingFailure_SweepsAllAndReturnsFirstFailure()
    {
        var firstFailure = new InvalidOperationException("first cleanup failure");
        var disposalOrder = new List<int>();
        IDisposable[] disposables =
        [
            new TrackingDisposable(0, disposalOrder),
            new TrackingDisposable(1, disposalOrder, firstFailure),
            new TrackingDisposable(2, disposalOrder),
        ];

        Exception? actual = PlanExecutor.DisposeDisposablesCapturingFailure(disposables);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(firstFailure));
            Assert.That(disposalOrder, Is.EqualTo(new[] { 2, 1, 0 }),
                "a teardown fault must not abort the reverse-order fused-stage cleanup sweep");
        });
    }

    [Test]
    public void ComputeInputCleanupFailure_ReleasesCompletedOutputAndInputOperation()
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute cleanup-failure contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                static ctx => ctx.CopySourceToDestination(),
                passCount: 1,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                structuralToken: "compute-input-cleanup-failure");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Compute(descriptor));
            using var pool = new RenderTargetPool();
            bool inputDisposed = false;
            RenderNodeOperation input = RenderNodeOperation.CreateLambda(
                s_bounds,
                canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
                onDispose: () => inputDisposed = true);
            var injected = new InvalidOperationException("compute input cleanup failed");

            IDisposable computeInputDisposeHook = PlanExecutor.UseTestHooks(hooks => hooks.ComputeInputDisposeFailure = injected);
            try
            {
                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                    plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery));
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(injected));
                    Assert.That(inputDisposed, Is.True,
                        "the source operation must be consumed even when compute input cleanup fails");
                    Assert.That(pool.LiveLeaseCount, Is.Zero,
                        "the completed compute output must not remain leased after cleanup fails");
                });
            }
            finally
            {
                computeInputDisposeHook.Dispose();
            }
        });
    }

    [Test]
    public void ComputeCpuFallbackInputCleanupFailure_ReleasesCompletedOutputAndInputOperation()
    {
        using var pool = new RenderTargetPool();
        var injected = new InvalidOperationException("CPU fallback input cleanup failed");
        ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
            dispatch: static _ => throw new AssertionException("dispatch must not run"),
            passCount: 1,
            BoundsContract.FullFrame,
            ComputeFallbackPolicy.Cpu(static session => session.Inputs[0].Draw(session.OpenCanvas())),
            structuralToken: "compute-cpu-input-cleanup-failure");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Compute(descriptor));
        bool inputDisposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            onDispose: () => inputDisposed = true);

        IDisposable computeFallbackHook = PlanExecutor.UseTestHooks(static hooks => hooks.ForceComputeFallback = true);
        IDisposable computeInputDisposeHook = PlanExecutor.UseTestHooks(hooks => hooks.ComputeInputDisposeFailure = injected);
        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery));
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(injected));
                Assert.That(inputDisposed, Is.True,
                    "the source operation must be consumed when CPU-fallback input cleanup fails");
                Assert.That(pool.LiveLeaseCount, Is.Zero,
                    "the completed CPU-fallback output must not remain leased after input cleanup fails");
            });
        }
        finally
        {
            computeInputDisposeHook.Dispose();
            computeFallbackHook.Dispose();
        }
    }

    [TestCase("copy-twice")]
    [TestCase("scratch-after-copy")]
    [TestCase("run-after-copy")]
    public void ComputeTerminalCopy_RejectsFurtherWork(string violation)
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute terminal-copy contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var shader = GLSLShader.Create(CopyShader);
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                ctx =>
                {
                    ctx.CopySourceToDestination();
                    switch (violation)
                    {
                        case "copy-twice":
                            ctx.CopySourceToDestination();
                            break;
                        case "scratch-after-copy":
                            ctx.AcquireColorScratch();
                            break;
                        case "run-after-copy":
                            ctx.Run(shader, ctx.Source, ctx.Destination, new PushConstants());
                            break;
                    }
                },
                passCount: 1,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                colorScratchCount: 1,
                structuralToken: "terminal-copy-" + violation);
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Compute(descriptor));
            using var pool = new RenderTargetPool();

            InvalidOperationException error = Assert.Catch<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.Contain("terminal").IgnoreCase);
                Assert.That(pool.LiveLeaseCount, Is.Zero);
            });
        });
    }

    [TestCase(1, 2)]
    [TestCase(2, 1)]
    public void StaticSplit_EmitCountMustExactlyMatchDeclaration(int declared, int actual)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            SplitNodeDescriptor descriptor = SplitNodeDescriptor.Static(
                emitter =>
                {
                    for (int i = 0; i < actual; i++)
                        emitter.Emit(s_bounds, static _ => { });
                },
                declared,
                structuralToken: $"split-count-{declared}-{actual}");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Split(descriptor));
            using var pool = new RenderTargetPool();

            InvalidOperationException error = Assert.Catch<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery))!;
            Assert.Multiple(() =>
            {
                Assert.That(error.Message, Does.Contain("branch"));
                Assert.That(error.Message, Does.Contain(declared.ToString()));
                Assert.That(pool.LiveLeaseCount, Is.Zero);
            });
        });
    }

    [Test]
    public void SplitInputCleanupFailure_DisposesDetachedSourceOperation()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            SplitNodeDescriptor descriptor = SplitNodeDescriptor.Static(
                emitter => emitter.Emit(emitter.Input.Bounds, session =>
                    session.Inputs[0].Draw(session.OpenCanvas(), default)),
                branchCount: 1,
                structuralToken: "split-input-cleanup-failure");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Split(descriptor));
            using var pool = new RenderTargetPool();
            bool inputDisposed = false;
            RenderNodeOperation input = RenderNodeOperation.CreateLambda(
                s_bounds,
                canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
                onDispose: () => inputDisposed = true);
            var injected = new InvalidOperationException("split input cleanup failed");

            IDisposable splitInputDisposeHook = PlanExecutor.UseTestHooks(hooks => hooks.SplitInputDisposeFailure = injected);
            try
            {
                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                    plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery));
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(injected));
                    Assert.That(inputDisposed, Is.True,
                        "the detached source operation must be consumed when split input cleanup fails");
                    Assert.That(pool.LiveLeaseCount, Is.Zero,
                        "already-emitted split branches must be released after cleanup fails");
                });
            }
            finally
            {
                splitInputDisposeHook.Dispose();
            }
        });
    }

    [Test]
    public void SplitRenderFailure_RemainsPrimaryWhenCleanupAlsoFails()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var primary = new InvalidOperationException("split callback failed");
            var cleanup = new InvalidOperationException("split cleanup failed");
            SplitNodeDescriptor descriptor = SplitNodeDescriptor.Static(
                _ => throw primary,
                branchCount: 1,
                structuralToken: "split-primary-failure");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Split(descriptor));
            using var pool = new RenderTargetPool();

            IDisposable splitInputDisposeHook = PlanExecutor.UseTestHooks(hooks => hooks.SplitInputDisposeFailure = cleanup);
            try
            {
                InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                    plan, resources, [Input()], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery));
                Assert.Multiple(() =>
                {
                    Assert.That(actual, Is.SameAs(primary));
                    Assert.That(pool.LiveLeaseCount, Is.Zero);
                });
            }
            finally
            {
                splitInputDisposeHook.Dispose();
            }
        });
    }

    [Test]
    public void GeometryInputCleanupFailure_ReleasesCompletedOutputAndInputOperation()
    {
        GeometryNodeDescriptor descriptor = GeometryNodeDescriptor.Create(
            static session => session.Inputs[0].Draw(session.OpenCanvas()),
            BoundsContract.Identity,
            structuralToken: "geometry-input-cleanup-failure");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Geometry(descriptor));
        using var pool = new RenderTargetPool();
        bool inputDisposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            onDispose: () => inputDisposed = true);
        var injected = new InvalidOperationException("geometry input cleanup failed");

        IDisposable geometryInputDisposeHook = PlanExecutor.UseTestHooks(hooks => hooks.GeometryInputDisposeFailure = injected);
        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery));
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(injected));
                Assert.That(inputDisposed, Is.True,
                    "the source operation must be consumed even when releasing its baked target fails");
                Assert.That(pool.LiveLeaseCount, Is.Zero,
                    "the already-rendered geometry output must not remain leased after input cleanup fails");
            });
        }
        finally
        {
            geometryInputDisposeHook.Dispose();
        }
    }

    [Test]
    public void DescriptorOperationCleanupFailure_ReleasesCompletedOutput()
    {
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Brightness(1.1f));
        using var pool = new RenderTargetPool();
        var injected = new InvalidOperationException("descriptor operation cleanup failed");
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            onDispose: () => throw injected);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(injected));
            Assert.That(pool.LiveLeaseCount, Is.Zero,
                "the completed descriptor output must be released when consuming its input operation fails");
        });
    }

    [Test]
    public void GeometryOperationCleanupFailure_ReleasesCompletedOutput()
    {
        GeometryNodeDescriptor descriptor = GeometryNodeDescriptor.Create(
            static session => session.Inputs[0].Draw(session.OpenCanvas()),
            BoundsContract.Identity,
            structuralToken: "geometry-operation-cleanup-failure");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Geometry(descriptor));
        using var pool = new RenderTargetPool();
        var injected = new InvalidOperationException("geometry operation cleanup failed");
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            onDispose: () => throw injected);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
            plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(injected));
            Assert.That(pool.LiveLeaseCount, Is.Zero,
                "the completed geometry output must be released when consuming its input operation fails");
        });
    }

    [Test]
    public void GeometryShrinkOutputCleanupFailure_ReleasesTightOutputAndInputOperation()
    {
        Rect tight = s_bounds.Deflate(4);
        GeometryNodeDescriptor descriptor = GeometryNodeDescriptor.Create(
            session =>
            {
                session.Inputs[0].Draw(session.OpenCanvas());
                session.SetOutputBounds(tight);
            },
            BoundsContract.Identity,
            structuralToken: "geometry-shrink-output-cleanup-failure");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Geometry(descriptor));
        using var pool = new RenderTargetPool();
        bool inputDisposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            onDispose: () => inputDisposed = true);
        var injected = new InvalidOperationException("geometry full output cleanup failed");

        IDisposable geometryOutputDisposeHook = PlanExecutor.UseTestHooks(hooks => hooks.GeometryOutputDisposeFailure = injected);
        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(() => PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics: null, pool, renderIntent: RenderIntent.Delivery));
            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(injected));
                Assert.That(inputDisposed, Is.True,
                    "the source operation must still be consumed after the tight output is rendered");
                Assert.That(pool.LiveLeaseCount, Is.Zero,
                    "the completed tight output must be released when full-output cleanup fails");
            });
        }
        finally
        {
            geometryOutputDisposeHook.Dispose();
        }
    }

    [Test]
    public void ForcedIdentityFallback_BetweenSkiaPassesDoesNotCountSyncs()
    {
        ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
            static _ => throw new AssertionException("dispatch must not run"),
            1,
            BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
            structuralToken: "forced-identity");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Saturate(1.2f).Compute(descriptor).Brightness(1.1f));
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        RenderNodeOperation input = Input();

        IDisposable computeFallbackHook = PlanExecutor.UseTestHooks(static hooks => hooks.ForceComputeFallback = true);
        try
        {
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics, pool, renderIntent: RenderIntent.Delivery);
            try
            {
                Assert.That(outputs, Has.Length.EqualTo(1));
                Assert.That(diagnostics.Snapshot().FlushSyncs, Is.Zero,
                    "a no-Vulkan identity pass performs no backend transition");
            }
            finally
            {
                RenderNodeOperation.DisposeAll(outputs);
            }
        }
        finally
        {
            computeFallbackHook.Dispose();
        }
    }

    [Test]
    public void ForcedIdentityFallback_ReturnsSameOperation()
    {
        ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
            static _ => throw new AssertionException("dispatch must not run"),
            1,
            BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
            structuralToken: "forced-identity-operation");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Compute(descriptor));
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        RenderNodeOperation input = Input();

        IDisposable computeFallbackHook = PlanExecutor.UseTestHooks(static hooks => hooks.ForceComputeFallback = true);
        try
        {
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics, pool, renderIntent: RenderIntent.Delivery);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(outputs, Has.Length.EqualTo(1));
                    Assert.That(outputs[0], Is.SameAs(input));
                    Assert.That(diagnostics.Snapshot().FlushSyncs, Is.Zero);
                });
            }
            finally
            {
                RenderNodeOperation.DisposeAll(outputs);
            }
        }
        finally
        {
            computeFallbackHook.Dispose();
        }
    }

    [Test]
    public void ComputeInputWithoutBackendTexture_DoesNotCountFlushSync()
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.RequireComputeCapable(graphics, "compute null-texture identity contract");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
                static _ => throw new AssertionException("dispatch must not run without a backend texture"),
                1,
                BoundsContract.FullFrame, ComputeFallbackPolicy.Identity,
                structuralToken: "null-texture-input");
            (CompiledPlan plan, FrameResources resources) = Compile(
                new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery)
                    .Compute(descriptor)
                    .Brightness(1.1f));
            var diagnostics = new PipelineDiagnostics();
            using var pool = new RenderTargetPool();
            pool.SetBackingFactoryForTest(static (width, height) =>
            {
                var info = new SKImageInfo(
                    width, height, SKColorType.RgbaF16, SKAlphaType.Premul, SKColorSpace.CreateSrgbLinear());
                return (SKSurface.Create(info) ?? throw new InvalidOperationException("raster surface unavailable"), null);
            });
            RenderNodeOperation input = Input();

            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics, pool, renderIntent: RenderIntent.Delivery);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(outputs, Has.Length.EqualTo(1));
                    Assert.That(diagnostics.Snapshot().FlushSyncs, Is.Zero,
                        "an identity compute must not invent a Vulkan-to-Skia transition before the following pass");
                });
            }
            finally
            {
                RenderNodeOperation.DisposeAll(outputs);
            }
        });
    }

    [Test]
    public void ForcedSkipFallback_DropsAndDisposesInputWithoutSyncs()
    {
        ComputeNodeDescriptor descriptor = ComputeNodeDescriptor.Create(
            static _ => throw new AssertionException("dispatch must not run"),
            1,
            BoundsContract.FullFrame, ComputeFallbackPolicy.Skip,
            structuralToken: "forced-skip");
        (CompiledPlan plan, FrameResources resources) = Compile(
            new EffectGraphBuilder(s_bounds, 1f, 1f, RenderIntent.Delivery).Compute(descriptor).Brightness(1.1f));
        var diagnostics = new PipelineDiagnostics();
        using var pool = new RenderTargetPool();
        bool disposed = false;
        RenderNodeOperation input = RenderNodeOperation.CreateLambda(
            s_bounds, static _ => { }, onDispose: () => disposed = true);

        IDisposable computeFallbackHook = PlanExecutor.UseTestHooks(static hooks => hooks.ForceComputeFallback = true);
        try
        {
            RenderNodeOperation[] outputs = PlanExecutor.Execute(
                plan, resources, [input], 1f, 1f, float.PositiveInfinity, diagnostics, pool, renderIntent: RenderIntent.Delivery);
            Assert.Multiple(() =>
            {
                Assert.That(outputs, Is.Empty);
                Assert.That(disposed, Is.True);
                Assert.That(diagnostics.Snapshot().FlushSyncs, Is.Zero,
                    "a no-Vulkan skip performs no backend transition");
            });
        }
        finally
        {
            computeFallbackHook.Dispose();
        }
    }

    private static (CompiledPlan Plan, FrameResources Resources) Compile(EffectGraphBuilder builder)
    {
        using EffectGraph graph = builder.Build();
        CompiledPlan plan = EffectGraphCompiler.Compile(graph, diagnostics: null);
        return (plan, EffectGraphCompiler.ResolveResources(plan, Rect.Invalid, 1f));
    }

    private static RenderNodeOperation Input()
        => RenderNodeOperation.CreateLambda(
            s_bounds,
            canvas => canvas.DrawRectangle(s_bounds, Brushes.Resource.White, null),
            hitTest: s_bounds.Contains);

    private sealed class TrackingDisposable(
        int id, List<int> disposalOrder, Exception? failure = null) : IDisposable
    {
        public void Dispose()
        {
            disposalOrder.Add(id);
            if (failure != null)
                throw failure;
        }
    }
}
